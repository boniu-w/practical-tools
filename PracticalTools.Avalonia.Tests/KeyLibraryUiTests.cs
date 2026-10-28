using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Desktop.Views;
using PracticalTools.GPG;
using PracticalTools.Security;
using PracticalTools.Sqlite;
using PracticalTools.Storage;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class KeyLibraryUiTests
{
    private sealed class Store : IDisposable
    {
        public AppDataPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "pt-library-ui", Guid.NewGuid().ToString("N")));
        private ServiceProvider _provider = null!;
        public KeyLibrary Library => _provider.GetRequiredService<KeyLibrary>();
        public Store() { Paths.EnsureDirectories(); SQLitePCL.Batteries_V2.Init(); Restart(); }
        public void Restart()
        {
            _provider?.Dispose();
            var services = new ServiceCollection();
            services.AddSingleton<LocalAesSecretProtector>(_ => new LocalAesSecretProtector(Paths.SecretKeyPath));
            services.AddSingleton<ISecretProtector>(p => p.GetRequiredService<LocalAesSecretProtector>());
            services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(Paths.DatabasePath)));
            services.AddSingleton<KeyLibrary>();
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

    private static void Click(Control control, string name) =>
        control.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task WaitUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("UI operation did not finish.");
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public async Task GeneratePersistsThenRestartedLibraryPowersBothCryptoPagesWithoutDisplayingSecrets()
    {
        using var store = new Store();
        var generate = new GenerateKeyPage();
        generate.Configure(store.Library);
        var window = new Window { Content = generate };
        window.Show();
        try
        {
            generate.FindControl<TextBox>("Identity")!.Text = "UI generated";
            generate.FindControl<TextBox>("Passphrase")!.Text = "ui-password";
            generate.FindControl<TextBox>("ConfirmPassphrase")!.Text = "ui-password";
            generate.FindControl<NumericUpDown>("KeyBits")!.Value = 2048;
            generate.FindControl<NumericUpDown>("ValidDays")!.Value = 3;
            Click(generate, "GenerateButton");
            await WaitUntil(() => generate.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.Contains("已保存", generate.FindControl<TextBlock>("Status")!.Text!);
            store.Restart();
            Assert.Single(await store.Library.ListAsync());

            var input = Path.Combine(store.Paths.Root, "input.txt");
            var encrypted = Path.Combine(store.Paths.Root, "encrypted.gpg");
            var output = Path.Combine(store.Paths.Root, "output.txt");
            await File.WriteAllTextAsync(input, "跨平台密钥库往返");
            var encrypt = new FileCryptoPage();
            encrypt.Configure(store.Library);
            window.Content = encrypt;
            await encrypt.ReloadKeysAsync();
            encrypt.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            encrypt.FindControl<TextBox>("InputPath")!.Text = input;
            encrypt.FindControl<TextBox>("OutputPath")!.Text = encrypted;
            Click(encrypt, "RunButton");
            await WaitUntil(() => encrypt.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.True(File.Exists(encrypted), encrypt.FindControl<TextBlock>("Status")!.Text);

            var decrypt = new FileCryptoPage { Decrypt = true };
            decrypt.Configure(store.Library);
            window.Content = decrypt;
            await decrypt.ReloadKeysAsync();
            decrypt.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            Assert.True(string.IsNullOrEmpty(decrypt.FindControl<TextBox>("KeyText")!.Text));
            Assert.True(string.IsNullOrEmpty(decrypt.FindControl<TextBox>("Passphrase")!.Text));
            decrypt.FindControl<TextBox>("InputPath")!.Text = encrypted;
            decrypt.FindControl<TextBox>("OutputPath")!.Text = output;
            Click(decrypt, "RunButton");
            await WaitUntil(() => decrypt.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.Equal("跨平台密钥库往返", await File.ReadAllTextAsync(output));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ImportCancellationWritesNothingAndDecryptPickerExcludesPublicOnlyKeys()
    {
        using var store = new Store();
        var owner = new Window();
        owner.Show();
        try
        {
            var import = new KeyImportWindow(store.Library);
            var result = import.ShowDialog<bool>(owner);
            import.Close(false);
            Assert.False(await result);
            Assert.Empty(await store.Library.ListAsync());
            var pair = GpgHelper.GeneratePgpKeyPair("public only", "password", 2048);
            await store.Library.ImportAsync(GpgHelper.ExportPublicKey(pair.pubRing), "", "", "");
            var decrypt = new FileCryptoPage { Decrypt = true };
            decrypt.Configure(store.Library);
            owner.Content = decrypt;
            await decrypt.ReloadKeysAsync();
            Assert.Empty(decrypt.FindControl<ComboBox>("LibraryKeys")!.Items);
            var encrypt = new FileCryptoPage();
            encrypt.Configure(store.Library);
            owner.Content = encrypt;
            await encrypt.ReloadKeysAsync();
            Assert.Single(encrypt.FindControl<ComboBox>("LibraryKeys")!.Items);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task MetadataEditAndDeleteCancellationAndConfirmationUseRealWindows()
    {
        using var store = new Store();
        var record = await store.Library.GenerateAsync("UI metadata", "ui-password", 2048, 0);
        var page = new KeyLibraryPage();
        page.Configure(store.Library);
        var owner = new Window { Content = page };
        owner.Show();
        try
        {
            await page.ReloadAsync();
            var grid = page.FindControl<DataGrid>("Keys")!;
            grid.SelectedIndex = 0;
            page.FindControl<TextBox>("KeyName")!.Text = "edited in UI";
            page.FindControl<TextBox>("Description")!.Text = "new description";
            Click(page, "SaveButton");
            await WaitUntil(() => page.FindControl<StackPanel>("Form")!.IsEnabled);
            Assert.Equal("edited in UI", (await store.Library.ListAsync()).Single().Name);
            grid.SelectedIndex = 0;
            Click(page, "DeleteButton");
            var cancelDialog = owner.OwnedWindows.OfType<ConfirmationWindow>().Single();
            cancelDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => page.FindControl<TextBlock>("Status")!.Text == "已取消删除。");
            Assert.Single(await store.Library.ListAsync());
            Click(page, "DeleteButton");
            var confirmDialog = owner.OwnedWindows.OfType<ConfirmationWindow>().Single();
            confirmDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => page.FindControl<StackPanel>("Form")!.IsEnabled && page.FindControl<TextBlock>("Status")!.Text == "已删除。");
            Assert.Empty(await store.Library.ListAsync());
            Assert.DoesNotContain("ui-password", page.FindControl<TextBlock>("Details")!.Text!);
        }
        finally { owner.Close(); }
    }
}
