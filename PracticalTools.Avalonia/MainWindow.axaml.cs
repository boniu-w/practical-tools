using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using PracticalTools.Storage;
using PracticalTools.GPG;
using PracticalTools.Migration;
using PracticalTools.DuplicateFiles.Scanning;
using PracticalTools.GPG.Folders;

namespace PracticalTools.Desktop;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, TabItem> _tabs = new();
    private readonly List<Button> _navButtons = new();

    // XAML runtime/design loader: no database or service container is created here.
    public MainWindow() : this(new AppDataPaths()) { }

    public MainWindow(AppDataPaths paths, KeyLibrary? library = null, MigrationPackageService? migration = null,
        IDuplicateScanner? scanner = null, ScanHistoryStore? scanHistory = null, IFolderCryptoService? folders = null, ToolRecords? records = null)
    {
        InitializeComponent();
        _tabs["home"] = HomeTab;
        _tabs["encrypt"] = EncryptTab;
        _tabs["decrypt"] = DecryptTab;
        _tabs["preview"] = PreviewTab;
        _tabs["organize"] = OrganizeTab;
        _tabs["generate"] = GenerateTab;
        _tabs["library"] = LibraryTab;
        _tabs["migration"] = MigrationTab;
        _tabs["scan"] = ScanTab;
        _tabs["records"] = FilesTab;
        _tabs["blacklist"] = BlacklistTab;
        _tabs["folder-encrypt"] = FolderEncryptTab;
        _tabs["folder-decrypt"] = FolderDecryptTab;
        _navButtons.AddRange(NavPanel.GetLogicalDescendants().OfType<Button>());

        DataLocation.Text = $"数据和日志目录：{paths.Root}";
        if (library != null)
        {
            EncryptPage.Configure(library);
            DecryptPage.Configure(library);
            GeneratePage.Configure(library);
            LibraryPage.Configure(library);
            FolderEncryptPage.Configure(library, folders);
            FolderDecryptPage.Configure(library, folders);
            PreviewPage.Configure(library, paths.Root);
        }
        if (migration != null) MigrationPage.Configure(migration);
        if (scanner != null) ScanPage.Configure(scanner, scanHistory);
        if (records != null)
        {
            FilesTab.Content = new Views.ToolRecordsPage(records, false);
            BlacklistTab.Content = new Views.ToolRecordsPage(records, true);
        }
    }

    /// <summary>切换左侧导航对应的页面，并同步选中状态。</summary>
    private void Navigate(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } button || !_tabs.TryGetValue(key, out var tab)) return;
        Tabs.SelectedItem = tab;
        foreach (var nav in _navButtons) nav.Classes.Remove("active");
        button.Classes.Add("active");
    }
}
