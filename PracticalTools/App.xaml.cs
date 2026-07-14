using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.DuplicateFiles.service;
using PracticalTools.GPG;
using PracticalTools.Sqlite;
using PracticalTools.vo;
using Serilog;
using Serilog.Events;

namespace PracticalTools;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string DbRelativePath = @"Sqlite\sqlite-PracticalTools.db";

    private static readonly string DbFullPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DbRelativePath);

    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>
    /// 全局互斥体，防止程序多开
    /// </summary>
    private static readonly Mutex AppMutex = new(false, @"Global\PracticalTools");

    private static readonly bool IsFirstInstance = AppMutex.WaitOne(TimeSpan.Zero, true);

    public App()
    {
        InitializeLogger();
        ServiceProvider = ConfigureServices();
    }

    /// <summary>
    /// 初始化 Serilog 日志
    /// </summary>
    private static void InitializeLogger()
    {
#if DEBUG
        var minLevel = LogEventLevel.Debug;
#else
        var minLevel = LogEventLevel.Information;
#endif
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .WriteTo.Console()
            .WriteTo.File("logs/log-.txt",
                rollingInterval: RollingInterval.Day,
                shared: true)        // 允许多进程写入同一日志文件
            .CreateLogger();
    }

    /// <summary>
    /// 配置 DI 容器，返回 IServiceProvider
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 数据库上下文（WPF 下建议自己管理 Scope）
        services.AddDbContext<MyDbContext>(options =>
            options.UseSqlite($"Data Source={DbFullPath}"));

        // 业务服务
        services.AddTransient<SQLiteDemo>();
        services.AddTransient<DuplicateFilesService>();
        services.AddTransient<GpgFileService>();

        // ViewModel & 主窗口
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsFirstInstance)
        {
            Log.Warning("检测到重复启动，已拒绝");
            MessageBox.Show("程序已在运行中，请勿重复打开。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Log.Information("应用启动");

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Closing += (_, args) =>
        {
            var result = MessageBox.Show("确定要退出程序吗？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                args.Cancel = true;
            }
        };
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (IsFirstInstance)
            {
                Log.Information("应用退出");
            }
            base.OnExit(e);
        }
        finally
        {
            if (IsFirstInstance)
            {
                AppMutex.Dispose();
            }
            Log.CloseAndFlush();
        }
    }
}