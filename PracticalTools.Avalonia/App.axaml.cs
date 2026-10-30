using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Activation.Service;
using PracticalTools.Desktop.Views;
using PracticalTools.Sqlite;
using PracticalTools.Storage;
using PracticalTools.Security;
using PracticalTools.GPG;
using PracticalTools.GPG.Preview;
using PracticalTools.Migration;
using PracticalTools.DuplicateFiles.Scanning;
using PracticalTools.GPG.Folders;
using Serilog;

namespace PracticalTools.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstanceLease? _instance;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var status = new TextBlock { Text = "正在初始化数据…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var close = new Button { Content = "退出" };
            close.Click += (_, _) => desktop.Shutdown();
            var startup = new Window
            {
                Title = "实用工具 · 启动", Width = 540, Height = 260,
                Content = new StackPanel { Margin = new Thickness(24), Spacing = 20, Children = { status, close } }
            };
            desktop.MainWindow = startup;
            desktop.Exit += (_, _) =>
            {
                _exiting = true;
                Log.Information("应用退出");
                _services?.Dispose();
                Log.CloseAndFlush();
                _instance?.Dispose();
            };
            startup.Opened += async (_, _) => await StartAsync(desktop, startup, status);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, Window startup, TextBlock status)
    {
        try
        {
            var paths = new AppDataPaths();
            paths.EnsureDirectories();
            try { _instance = SingleInstanceLease.Acquire(paths.InstanceLockPath); }
            catch (IOException ex) when (File.Exists(paths.InstanceLockPath))
            {
                status.Text = $"无法取得运行锁，程序可能已在运行。请关闭已有窗口后重试。\n{ex.Message}";
                return;
            }
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                .WriteTo.File(Path.Combine(paths.LogsDirectory, "log-.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
            Log.Information("Avalonia 应用启动，数据目录：{DataDirectory}", paths.Root);
            try
            {
                int removed = PreviewService.CleanupStaleWorkspaces(paths.Root);
                if (removed > 0) Log.Information("已清理 {Count} 个遗留预览工作区", removed);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "清理遗留预览工作区失败");
            }
            SQLitePCL.Batteries_V2.Init();
            var services = new ServiceCollection();
            services.AddSingleton(paths);
            services.AddSingleton<LocalAesSecretProtector>(_ => new LocalAesSecretProtector(paths.SecretKeyPath));
            services.AddSingleton<ISecretProtector>(provider => provider.GetRequiredService<LocalAesSecretProtector>());
            services.AddDbContext<MyDbContext>(options => options.UseSqlite(DatabaseInitializer.ConnectionString(paths.DatabasePath)));
            services.AddSingleton<ActivationService>();
            services.AddSingleton<KeyLibrary>();
            services.AddSingleton<MigrationPackageService>();
            services.AddSingleton<IDuplicateScanner, DuplicateScanner>();
            services.AddSingleton<ScanHistoryStore>();
            services.AddSingleton<IFolderCryptoService, FolderCryptoService>();
            services.AddTransient<MainWindow>();
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var activated = await Task.Run(() =>
            {
                using var scope = _services.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                DatabaseInitializer.InitializeProtectedStore(database,
                    _services.GetRequiredService<LocalAesSecretProtector>());
                return _services.GetRequiredService<ActivationService>().IsActivated();
            });
            if (_exiting) return;
            if (activated)
                ShowMainWindow(desktop, startup);
            else
            {
                var activation = new ActivationWindow(_services.GetRequiredService<ActivationService>(), paths);
                activation.ActivationSucceeded += () => ShowMainWindow(desktop, activation);
                desktop.MainWindow = activation;
                activation.Show();
                startup.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "初始化失败");
            if (!_exiting) status.Text = $"启动失败，请查看用户数据目录下的日志。\n{ex.Message}";
        }
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop, Window previous)
    {
        var main = _services!.GetRequiredService<MainWindow>();
        desktop.MainWindow = main;
        main.Show();
        previous.Close();
        Log.Information("已打开主窗口");
    }
}
