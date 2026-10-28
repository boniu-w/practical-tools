using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Desktop.Views;
using PracticalTools.GPG;
using PracticalTools.Migration;
using PracticalTools.Security;
using PracticalTools.Sqlite;
using PracticalTools.Storage;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class MigrationUiTests
{
    private sealed class Store : IDisposable
    {
        public AppDataPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "pt-migration-ui", Guid.NewGuid().ToString("N")));
        private readonly ServiceProvider _provider;
        public KeyLibrary Library => _provider.GetRequiredService<KeyLibrary>();
        public MigrationPackageService Packages => _provider.GetRequiredService<MigrationPackageService>();
        public Store()
        {
            Paths.EnsureDirectories();
            SQLitePCL.Batteries_V2.Init();
            var services = new ServiceCollection();
            services.AddSingleton<LocalAesSecretProtector>(_ => new LocalAesSecretProtector(Paths.SecretKeyPath));
            services.AddSingleton<ISecretProtector>(p => p.GetRequiredService<LocalAesSecretProtector>());
            services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(Paths.DatabasePath)));
            services.AddSingleton<KeyLibrary>();
            services.AddSingleton<MigrationPackageService>();
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = _provider.CreateScope();
            DatabaseInitializer.InitializeProtectedStore(scope.ServiceProvider.GetRequiredService<MyDbContext>(),
                _provider.GetRequiredService<LocalAesSecretProtector>());
        }
        public void Dispose()
        {
            _provider.Dispose(); SqliteConnection.ClearAllPools();
            Directory.Delete(Paths.Root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task PreviewRequiresConfirmationAndPasswordChangeInvalidatesIt()
    {
        using var source = new Store();
        using var target = new Store();
        var saved = await source.Library.GenerateAsync("UI migration", "key-password", 2048, 0);
        var package = Path.Combine(source.Paths.Root, "transfer.ptmigration");
        await source.Packages.ExportCurrentAsync(package, "migration-password-2026");
        var page = new MigrationPage();
        page.Configure(target.Packages);
        var window = new Window { Content = page };
        window.Show();
        try
        {
            var confirm = page.FindControl<Button>("ConfirmImportButton")!;
            page.FindControl<TextBox>("PackagePath")!.Text = package;
            page.FindControl<TextBox>("Password")!.Text = "migration-password-2026";
            Assert.False(confirm.IsEnabled);
            page.FindControl<Button>("PreviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => page.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.True(confirm.IsEnabled, page.FindControl<TextBlock>("Status")!.Text);
            Assert.Contains(saved.Fingerprint, page.FindControl<TextBlock>("PreviewText")!.Text!);
            Assert.Empty(await target.Library.ListAsync());
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => page.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.Single(await target.Library.ListAsync());
            Assert.Contains("已导入密钥 1", page.FindControl<TextBlock>("Status")!.Text!);
            Assert.False(confirm.IsEnabled);
            page.FindControl<Button>("PreviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => page.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.True(confirm.IsEnabled);
            page.FindControl<TextBox>("Password")!.Text = "wrong-password-2026";
            Assert.False(confirm.IsEnabled);
            page.FindControl<Button>("PreviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => page.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.False(confirm.IsEnabled);
            Assert.Contains("错误", page.FindControl<TextBlock>("Status")!.Text!);
            Assert.Single(await target.Library.ListAsync());
        }
        finally { window.Close(); }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("Migration UI did not finish.");
            await Task.Delay(20);
        }
    }
}
