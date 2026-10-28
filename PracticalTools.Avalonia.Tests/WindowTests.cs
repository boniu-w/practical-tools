using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Activation.Service;
using PracticalTools.Desktop;
using PracticalTools.Desktop.Views;
using PracticalTools.Sqlite;
using PracticalTools.Storage;
using PracticalTools.Security;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(PracticalTools.Desktop.Tests.TestAppBuilder))]

namespace PracticalTools.Desktop.Tests;

public class TestAppBuilder
{
    // No desktop lifetime: load the real resources without opening the production database.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class WindowTests
{
    [AvaloniaFact]
    public async Task ActivationRejectsInvalidCodeAndEmitsSuccessOnlyAfterDatabaseWrite()
    {
        var paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), "pt-ui-tests", Guid.NewGuid().ToString("N")));
        paths.EnsureDirectories();
        SQLitePCL.Batteries_V2.Init();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<LocalAesSecretProtector>(_ => new LocalAesSecretProtector(paths.SecretKeyPath));
            services.AddSingleton<ISecretProtector>(p => p.GetRequiredService<LocalAesSecretProtector>());
            services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(paths.DatabasePath)));
            services.AddSingleton<ActivationService>();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using (var scope = provider.CreateScope())
                DatabaseInitializer.InitializeProtectedStore(scope.ServiceProvider.GetRequiredService<MyDbContext>(),
                    provider.GetRequiredService<LocalAesSecretProtector>());
            var service = provider.GetRequiredService<ActivationService>();
            var window = new ActivationWindow(service, paths);
            var succeeded = false;
            window.ActivationSucceeded += () => succeeded = true;
            window.Show();
            try
            {
                var code = window.FindControl<TextBox>("Code")!;
                var button = window.FindControl<Button>("ActivateButton")!;
                code.Text = "invalid";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => button.IsEnabled);
                Assert.False(succeeded);
                Assert.False(service.IsActivated());
                Assert.Contains("无效", window.FindControl<TextBlock>("Status")!.Text!);
                code.Text = "GPGKEY-UTIL-PRO-VIP";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => button.IsEnabled);
                Assert.True(succeeded);
                Assert.True(service.IsActivated());
            }
            finally { window.Close(); }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void MainWindowLoadsWithInjectedDataPaths()
    {
        var paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), "pt-ui-preview"));
        var window = new MainWindow(paths);
        window.Show();
        try { Assert.Contains(paths.Root, window.FindControl<TextBlock>("DataLocation")!.Text!); }
        finally { window.Close(); }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > timeout) throw new TimeoutException("UI operation did not finish.");
            await Task.Delay(20);
        }
    }
}
