using Avalonia.Controls;
using PracticalTools.Storage;
using PracticalTools.GPG;
using PracticalTools.Migration;
using PracticalTools.DuplicateFiles.Scanning;
using PracticalTools.GPG.Folders;

namespace PracticalTools.Desktop;

public partial class MainWindow : Window
{
    // XAML runtime/design loader: no database or service container is created here.
    public MainWindow() : this(new AppDataPaths()) { }

    public MainWindow(AppDataPaths paths, KeyLibrary? library = null, MigrationPackageService? migration = null,
        IDuplicateScanner? scanner = null, ScanHistoryStore? scanHistory = null, IFolderCryptoService? folders = null, ToolRecords? records = null)
    {
        InitializeComponent();
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
}
