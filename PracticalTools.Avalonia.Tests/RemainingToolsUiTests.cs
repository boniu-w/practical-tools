using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PracticalTools.Desktop.Views;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class RemainingToolsUiTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Control root, string name) => Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    [AvaloniaFact]
    public async Task BlacklistFormCreatesRecordAndLoadsItFromDatabase()
    {
        using var store = new FolderCryptoUiTests.Store();
        var page = new ToolRecordsPage(store.Records, true); var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadAsync(); await Task.Delay(30);
            Find<TextBox>(page, "Field0").Text = "test platform"; Find<TextBox>(page, "Field1").Text = "test author";
            Click(page, "Save");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((await store.Records.BlacklistAsync("")).Count == 0) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(30); }
            await Task.Delay(100); Assert.Single(await store.Records.BlacklistAsync("test author"));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task OrganizerPreviewIsClearedWhenInputChangesAndNeverMovesWithoutConfirmation()
    {
        using var store = new FolderCryptoUiTests.Store();
        var page = new FileOrganizerPage(); var window = new Window { Content = page }; window.Show();
        try
        {
            Find<TextBox>(page, "FolderPath").Text = store.Source; Find<TextBox>(page, "Suffixes").Text = "txt";
            Click(page, "Preview"); var table = Find<DataGrid>(page, "MovePreview");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (table.ItemsSource == null) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(30); }
            Assert.True(File.Exists(Path.Combine(store.Source, "中文.txt")));
            Find<TextBox>(page, "Suffixes").Text = "jpg"; Assert.Null(table.ItemsSource);
        }
        finally { window.Close(); }
    }
}
