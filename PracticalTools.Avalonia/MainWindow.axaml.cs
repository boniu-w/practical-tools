using Avalonia.Controls;
using PracticalTools.Storage;

namespace PracticalTools.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(AppDataPaths paths)
    {
        InitializeComponent();
        DataLocation.Text = $"数据和日志目录：{paths.Root}";
    }
}
