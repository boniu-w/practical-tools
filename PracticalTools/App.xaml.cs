using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    private void ConfigureServices(IServiceCollection services)
    {
        // 注册配置服务
        services.AddSingleton<IConfiguration>(_configuration);
        
        // 注册数据库上下文（根据实际情况修改）
        services.AddDbContext<MyDbContext>(options => 
            options.UseSqlite(_configuration.GetConnectionString("DefaultConnection")));
        
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