using System.Windows;
using Microsoft.Win32;
using PracticalTools.Blacklist.Model;
using PracticalTools.Sqlite;
using PracticalTools.vo;
using Window = System.Windows.Window;

namespace PracticalTools;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // public MainWindow()
    // {
    //     var viewModel = ((App)Application.Current)._serviceProvider.GetRequiredService<MainViewModel>();
    //     DataContext = viewModel;
    //     InitializeComponent();
    // }

    private readonly SQLiteDemo _sqLiteDemo;
    private readonly MainViewModel _mainViewModel;

    public MainWindow(MainViewModel mainViewModel, SQLiteDemo sqLiteDemo)
    {
        InitializeComponent();
        DataContext = mainViewModel;
        _mainViewModel = mainViewModel;
        _sqLiteDemo = sqLiteDemo;
    }

    /*****************************************************************************************************************************/

    public void SqliteTest(Object sender, EventArgs args)
    {
        // SQLiteDemo demo = new SQLiteDemo();
        // demo.Open();
        // demo.CreateTableBlackList();
        // demo.InsertTheFile(null);
        // demo.InsertBlackList();
        //
        // demo.Select();
        // demo.Close();

        // demo.List();

        // _sqLiteDemo.InsertBlackList();
    }

    public void List(Object sender, EventArgs args)
    {
        List<BlacklistEntry> list = _mainViewModel.Blacklists.ToList();
        Console.WriteLine(list.Count);
    }

    private void PickFile(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog();
        if (folderDialog.ShowDialog() == true)
        {
            string selectedFolder = folderDialog.FolderName;
            TheFilePath.Text = selectedFolder;
            TheFilePath.IsReadOnly = true;
        }
    }

    private void ScanTheFolder(object sender, RoutedEventArgs e)
    {
        string theFilePath = "";
        if (TheFilePath.Text.Length == 0)
        {
            MessageBoxResult messageBoxResult = MessageBox.Show("没有选择任何文件夹, 将扫描整个电脑",
                "提示",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (messageBoxResult == MessageBoxResult.OK)
            {
                Scan(theFilePath);
            }
        }
        else
        {
            theFilePath = TheFilePath.Text;
            Scan(theFilePath);
        }
    }

    private void UnPickFile(object sender, RoutedEventArgs e)
    {
        TheFilePath.Text = "";
        TheFilePath.IsReadOnly = false;
    }

    private void Scan(string theFilePath)
    {
        Console.WriteLine(theFilePath);
    }
}