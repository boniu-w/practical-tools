using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.DuplicateFiles.service;
using PracticalTools.Sqlite;
using PracticalTools.vo;

namespace PracticalTools;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public App()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        // 配置服务
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    /// <summary>
    /// AddSingleton
    /// 服务实例在‌整个应用程序生命周期‌内只创建一次，所有请求共享同一个实例‌12。例如：
    /// 特点‌：首次请求时创建实例，后续所有请求复用同一对象‌
    /// 适用场景‌：全局共享且无状态的服务（如配置类、日志服务）‌
    ///
    /// AddTransient
    /// 每次请求（包括同一请求内的多次解析）都会‌创建新实例‌，实例用完即释放‌
    /// 特点‌：每次解析均生成独立实例，无跨请求共享‌
    /// 适用场景‌：轻量级、无状态且需隔离资源的服务（如临时计算类）‌
    /// </summary>
    /// <param name="services"></param>
    private void ConfigureServices(IServiceCollection services)
    {
        // 注册配置服务
        services.AddSingleton<IConfiguration>(_configuration);

        // 注册数据库上下文（根据实际情况修改）
        services.AddDbContext<MyDbContext>(options =>
            options.UseSqlite(_configuration.GetConnectionString("DefaultConnection")));

        services.AddTransient<SQLiteDemo>();
        services.AddTransient<DuplicateFilesService>();
        
        // 注册 ViewModel
        services.AddTransient<MainViewModel>();
        // 注册主窗口
        services.AddTransient<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
        base.OnStartup(e);
    }
}