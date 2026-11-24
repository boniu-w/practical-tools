using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Desktop.Views;
using PracticalTools.DuplicateFiles.Scanning;
using PracticalTools.Sqlite;
using PracticalTools.Storage;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class DuplicateScanUiTests
{
    private sealed class PermanentTestRemoval : IFileRemoval
    {
        public bool CanRecycle => false;
        public void Remove(string path, FileRemovalMode mode) => File.Delete(path);
    }
    private sealed class BlockingScanner : IDuplicateScanner
    {
        public bool CanRecycle => false;
        public bool Started { get; private set; }
        public async Task<DuplicateScan> ScanAsync(IReadOnlyList<string> roots, string extensions,
            IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        { Started = true; await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); }
        public Task<FileRemovalReport> RemoveAsync(DuplicateScan scan, IReadOnlyList<ScannedFile> selected, FileRemovalMode mode) => throw new NotSupportedException();
    }
    private sealed class Store : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("pt-scan-ui-").FullName;
        private readonly ServiceProvider _provider;
        public string Content { get; }
        public ScanHistoryStore History => _provider.GetRequiredService<ScanHistoryStore>();
        public Store()
        {
            Content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
            File.WriteAllText(Path.Combine(Content, "a.txt"), "same");
            File.WriteAllText(Path.Combine(Content, "b.txt"), "same");
            File.WriteAllText(Path.Combine(Content, "unique.txt"), "longer unique content");
            var paths = new AppDataPaths(Path.Combine(_root, "data")); paths.EnsureDirectories();
            SQLitePCL.Batteries_V2.Init();
            var services = new ServiceCollection();
            services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(paths.DatabasePath)));
            services.AddSingleton<ScanHistoryStore>();
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = _provider.CreateScope();
            DatabaseInitializer.Initialize(scope.ServiceProvider.GetRequiredService<MyDbContext>());
        }
        public void Dispose()
        { _provider.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(_root, recursive: true); }
    }

    private static void Click(Control root, string name) => root.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Answer(Window owner, string name)
    {
        var dialog = owner.OwnedWindows.OfType<ConfirmationWindow>().Single();
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        { if (DateTime.UtcNow > timeout) throw new TimeoutException("Scan UI did not finish."); await Task.Delay(20); }
    }

    [AvaloniaFact]
    public async Task PathColumnFillsUnusedHeaderSpace()
    {
        var page = new DuplicateScanPage();
        var window = new Window { Content = page, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            var grid = page.FindControl<DataGrid>("Results")!;
            var headers = grid.GetVisualDescendants()
                .OfType<Avalonia.Controls.Primitives.DataGridColumnHeadersPresenter>().Single();
            await WaitUntil(() => headers.Bounds.Width > 800 &&
                Math.Abs(headers.Bounds.Width - grid.Columns.Sum(column => column.ActualWidth)) < 1);

            grid.Columns[1].Width = new DataGridLength(100);
            await WaitUntil(() => Math.Abs(headers.Bounds.Width - grid.Columns.Sum(column => column.ActualWidth)) < 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ScanHistorySmartSelectionCancelAndConfirmedRemovalWorkTogether()
    {
        using var store = new Store();
        var page = new DuplicateScanPage();
        page.Configure(new DuplicateScanner(new PermanentTestRemoval()), store.History);
        var window = new Window { Content = page }; window.Show();
        try
        {
            page.FindControl<TextBox>("Roots")!.Text = store.Content;
            Click(page, "ScanButton");
            await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            var grid = page.FindControl<DataGrid>("Results")!;
            Assert.Equal(2, grid.ItemsSource.Cast<ScannedFile>().Count());
            Assert.Contains("记录已保存", page.FindControl<TextBlock>("Status")!.Text!);
            Assert.Equal(3, (await store.History.RecentAsync()).Count);
            Click(page, "SuggestButton");
            Assert.Single(grid.SelectedItems.Cast<ScannedFile>());
            Click(page, "RemoveButton");
            var review = string.Join("\n", window.OwnedWindows.OfType<ConfirmationWindow>().Single()
                .GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("永久删除（不可恢复）", review);
            Assert.Contains(grid.SelectedItems.Cast<ScannedFile>().Single().FilePath, review);
            Answer(window, "CancelButton");
            await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.Equal(3, Directory.GetFiles(store.Content).Length);
            Click(page, "RemoveButton");
            Answer(window, "ConfirmButton");
            await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.Equal(2, Directory.GetFiles(store.Content).Length);
            Assert.Empty(grid.ItemsSource.Cast<ScannedFile>());
            Assert.Contains("已移除 1", page.FindControl<TextBlock>("Status")!.Text!);
            // Stored paths are an audit of the completed scan, never a fresh deletion source.
            Assert.Equal(3, (await store.History.RecentAsync()).Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CancellationRestoresInputsAndPublishesNoPartialResultsOrHistory()
    {
        using var store = new Store();
        var scanner = new BlockingScanner();
        var page = new DuplicateScanPage(); page.Configure(scanner, store.History);
        var window = new Window { Content = page }; window.Show();
        try
        {
            page.FindControl<TextBox>("Roots")!.Text = store.Content;
            Click(page, "ScanButton");
            await WaitUntil(() => scanner.Started);
            Click(page, "CancelButton");
            await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.Contains("已取消", page.FindControl<TextBlock>("Status")!.Text!);
            Assert.Empty(page.FindControl<DataGrid>("Results")!.ItemsSource.Cast<ScannedFile>());
            Assert.False(page.FindControl<StackPanel>("Actions")!.IsEnabled);
            Assert.Empty(await store.History.RecentAsync());
            Assert.Equal(3, Directory.GetFiles(store.Content).Length);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChangedFileRemainsVisibleWhenRemovalFails()
    {
        using var store = new Store();
        var page = new DuplicateScanPage(); page.Configure(new DuplicateScanner(new PermanentTestRemoval()), store.History);
        var window = new Window { Content = page }; window.Show();
        try
        {
            page.FindControl<TextBox>("Roots")!.Text = store.Content;
            Click(page, "ScanButton");
            await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Click(page, "SuggestButton");
            var grid = page.FindControl<DataGrid>("Results")!;
            var selected = grid.SelectedItems.Cast<ScannedFile>().Single();
            File.WriteAllText(selected.FilePath, "changed content");
            Click(page, "RemoveButton"); Answer(window, "ConfirmButton");
            await WaitUntil(() => page.FindControl<StackPanel>("Inputs")!.IsEnabled);
            Assert.True(File.Exists(selected.FilePath));
            Assert.Equal(2, grid.ItemsSource.Cast<ScannedFile>().Count());
            Assert.Contains("已移除 0", page.FindControl<TextBlock>("Status")!.Text!);
            Assert.Contains("变化", page.FindControl<TextBlock>("Warnings")!.Text!);
        }
        finally { window.Close(); }
    }
}
