using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Desktop.Views;
using PracticalTools.GPG;
using PracticalTools.GPG.Folders;
using PracticalTools.Security;
using PracticalTools.Sqlite;
using PracticalTools.Storage;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class FolderCryptoUiTests
{
    internal sealed class Store : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("pt-folder-ui-").FullName;
        private readonly ServiceProvider _provider;
        public string Source { get; }
        public string Output(string name) => Path.Combine(_root, name);
        public KeyLibrary Library => _provider.GetRequiredService<KeyLibrary>();
        public ToolRecords Records => new(_provider.GetRequiredService<IServiceScopeFactory>());
        public Store()
        {
            Source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
            Directory.CreateDirectory(Path.Combine(Source, "empty"));
            File.WriteAllText(Path.Combine(Source, "中文.txt"), "folder UI roundtrip");
            var paths = new AppDataPaths(Path.Combine(_root, "data")); paths.EnsureDirectories();
            SQLitePCL.Batteries_V2.Init();
            var services = new ServiceCollection();
            services.AddSingleton<LocalAesSecretProtector>(_ => new LocalAesSecretProtector(paths.SecretKeyPath));
            services.AddSingleton<ISecretProtector>(p => p.GetRequiredService<LocalAesSecretProtector>());
            services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(paths.DatabasePath)));
            services.AddSingleton<KeyLibrary>();
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = _provider.CreateScope();
            DatabaseInitializer.InitializeProtectedStore(scope.ServiceProvider.GetRequiredService<MyDbContext>(),
                _provider.GetRequiredService<LocalAesSecretProtector>());
        }
        public void Dispose() { _provider.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); }
    }
    private sealed class BlockingService : IFolderCryptoService
    {
        public bool Started { get; private set; }
        public async Task<FolderCryptoResult> EncryptAsync(string source, string outputFile, string publicKey,
            IProgress<FolderCryptoProgress>? progress = null, CancellationToken cancellationToken = default)
        { Started = true; await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); }
        public Task<FolderCryptoResult> DecryptAsync(string inputFile, string outputDirectory, string secretKey, string passphrase,
            IProgress<FolderCryptoProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private static void Click(Control root, string name) => root.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task WaitUntil(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!condition()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Folder UI did not finish."); await Task.Delay(20); }
    }

    [AvaloniaFact]
    public async Task StoredKeyEncryptAndDecryptPagesRestoreNestedAndEmptyDirectories()
    {
        using var store = new Store();
        await store.Library.GenerateAsync("folder UI", "folder-password", 2048, 0);
        var encrypt = new FolderCryptoPage(); encrypt.Configure(store.Library);
        var window = new Window { Content = encrypt }; window.Show();
        try
        {
            await encrypt.ReloadKeysAsync();
            encrypt.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            encrypt.FindControl<TextBox>("InputPath")!.Text = store.Source;
            encrypt.FindControl<TextBox>("OutputPath")!.Text = store.Output("archive.zip.gpg");
            Click(encrypt, "RunButton");
            await WaitUntil(() => encrypt.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.Contains("成功", encrypt.FindControl<TextBlock>("Status")!.Text!);
            var decrypt = new FolderCryptoPage { Decrypt = true }; decrypt.Configure(store.Library);
            window.Content = decrypt; await decrypt.ReloadKeysAsync();
            decrypt.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            decrypt.FindControl<TextBox>("InputPath")!.Text = store.Output("archive.zip.gpg");
            decrypt.FindControl<TextBox>("OutputPath")!.Text = store.Output("restored");
            Click(decrypt, "RunButton");
            await WaitUntil(() => decrypt.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.Contains("成功", decrypt.FindControl<TextBlock>("Status")!.Text!);
            Assert.Equal("folder UI roundtrip", File.ReadAllText(Path.Combine(store.Output("restored"), "中文.txt")));
            Assert.True(Directory.Exists(Path.Combine(store.Output("restored"), "empty")));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CancelRestoresControlsWithoutOutputAndExistingOutputIsPreserved()
    {
        using var store = new Store();
        await store.Library.GenerateAsync("folder UI", "folder-password", 2048, 0);
        var blocked = new BlockingService();
        var page = new FolderCryptoPage(); page.Configure(store.Library, blocked);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            page.FindControl<TextBox>("InputPath")!.Text = store.Source;
            page.FindControl<TextBox>("OutputPath")!.Text = store.Output("cancelled.gpg");
            Click(page, "RunButton"); await WaitUntil(() => blocked.Started);
            Click(page, "CancelButton"); await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.Contains("已取消", page.FindControl<TextBlock>("Status")!.Text!);
            Assert.False(File.Exists(store.Output("cancelled.gpg")));
            page.Configure(store.Library, new FolderCryptoService());
            var output = store.Output("existing.gpg"); File.WriteAllText(output, "keep original");
            page.FindControl<TextBox>("OutputPath")!.Text = output;
            Click(page, "RunButton"); await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.Contains("已存在", page.FindControl<TextBlock>("Status")!.Text!);
            Assert.Equal("keep original", File.ReadAllText(output));
        }
        finally { window.Close(); }
    }
}
