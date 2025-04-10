using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Sqlite;
using PracticalTools.vo;

namespace PracticalTools;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        ServiceProvider = serviceCollection.BuildServiceProvider();

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    /// <summary>
    /// 依赖注入
    /// </summary>
    /// <param name="services"></param>
    private void ConfigureServices(ServiceCollection services)
    {
        // 注册数据库上下文
        services.AddDbContext<MyDbContext>();
        // 注册视图模型
        services.AddTransient<MainViewModel>();
        // 注册主窗口
        services.AddTransient<MainWindow>();
    }
}