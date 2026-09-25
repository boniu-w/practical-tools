using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Activation.Service;
using PracticalTools.Constant;
using PracticalTools.DuplicateFiles.service;
using PracticalTools.GPG;
using PracticalTools.Sqlite;
using PracticalTools.Views;
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

    private static readonly string DbFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DbRelativePath);

    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>
    /// 全局互斥体，防止程序多开
    /// </summary>
    private static readonly Mutex AppMutex = new(false, @"Global\PracticalTools");

    private static readonly bool IsFirstInstance = TryAcquireMutex();

    /// <summary>
    /// 尝试获取全局互斥体，判断是否为第一个实例。
    /// 若之前的实例异常退出导致互斥体被遗弃，则视为获取成功。
    /// </summary>
    private static bool TryAcquireMutex()
    {
        try
        {
            return AppMutex.WaitOne(TimeSpan.Zero, true);
        }
        catch (AbandonedMutexException)
        {
            // 之前的实例异常退出，遗弃了互斥体；当前进程已成功获取，视为第一个实例
            return true;
        }
    }

    public App()
    {
        // 防止激活窗口关闭时无其他窗口导致 Application 自动退出
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 初始化 SQLCipher 引擎（必须在任何 SQLite 操作之前调用）
        SQLitePCL.Batteries_V2.Init();

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
                shared: true) // 允许多进程写入同一日志文件
            .CreateLogger();
    }

    /// <summary>
    /// 配置 DI 容器，返回 IServiceProvider
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 数据库上下文（SQLCipher 加密）
        services.AddDbContext<MyDbContext>(options => options.UseSqlite(
            $"Data Source={DbFullPath};Password={CommonConst.DbPassword}"));

        // 业务服务
        services.AddTransient<SQLiteDemo>();
        services.AddTransient<DuplicateFilesService>();
        services.AddTransient<GpgFileService>();
        services.AddTransient<ActivationService>();

        // ViewModel & 主窗口
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 检查激活状态，未激活则弹出激活窗口
    /// </summary>
    /// <returns>true 已激活或激活成功，false 用户放弃激活</returns>
    private static bool CheckActivation()
    {
        var activationService = ServiceProvider.GetRequiredService<ActivationService>();

        if (activationService.IsActivated())
            return true;

        // 弹出激活窗口
        var activationWindow = new ActivationWindow();
        bool? result = activationWindow.ShowDialog();

        if (result == true)
        {
            Log.Information("软件激活成功");
            MessageBox.Show("激活成功！感谢使用。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        Log.Warning("用户未激活，程序退出");
        MessageBox.Show("软件未激活，无法使用。", "提示",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
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

        // 确保数据库目录存在（SQLite 不会自动创建父目录）
        var dbDir = Path.GetDirectoryName(DbFullPath);
        if (!Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir!);
            Log.Information("已创建数据库目录: {DbDir}", dbDir);
        }

        // 迁移旧版未加密数据库到 SQLCipher 加密格式
        DatabaseMigrationHelper.MigrateToEncrypted(DbFullPath);

        // 确保数据库和表存在（新电脑首次运行自动创建）
        using (var scope = ServiceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            db.Database.EnsureCreated();

            // 确保 ActivationRecord 表存在（旧数据库升级兼容）
            db.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ActivationRecord (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActivationCode TEXT NOT NULL,
                    ActivateTime TEXT,
                    CreateTime TEXT
                );");
        }

        // 检查激活状态
        if (!CheckActivation())
        {
            Shutdown();
            return;
        }

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
        mainWindow.Closed += (_, _) => Shutdown();
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