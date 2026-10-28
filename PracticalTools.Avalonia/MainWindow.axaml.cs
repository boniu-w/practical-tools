using Avalonia.Controls;
using PracticalTools.Storage;
using PracticalTools.GPG;

namespace PracticalTools.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(AppDataPaths paths, KeyLibrary? library = null)
    {
        InitializeComponent();
        DataLocation.Text = $"数据和日志目录：{paths.Root}";
        if (library != null)
        {
            EncryptPage.Configure(library);
            DecryptPage.Configure(library);
            GeneratePage.Configure(library);
            LibraryPage.Configure(library);
        }
    }
}
