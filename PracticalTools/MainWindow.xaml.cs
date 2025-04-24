using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.DuplicateFiles.service;
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
    private DuplicateFilesService _duplicateFilesService;

    public MainWindow(MainViewModel mainViewModel, SQLiteDemo sqLiteDemo, DuplicateFilesService duplicateFilesService)
    {
        InitializeComponent();
        DataContext = mainViewModel;
        _mainViewModel = mainViewModel;
        _sqLiteDemo = sqLiteDemo;
        _duplicateFilesService = duplicateFilesService;
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
        List<DuplicateFilesEntry> entries = _duplicateFilesService.GetAllFile(theFilePath);
        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntry>(entries);
    }

    private void SearchDuplicateFile(object sender, RoutedEventArgs e)
    {
        SearchText.IsReadOnly = true;
        string searchTextText = SearchText.Text?.Trim();
        if (string.IsNullOrEmpty(searchTextText))
        {
            return;
        }

        List<DuplicateFilesEntry> list = _mainViewModel.FileList
            .Where(e => e.FileName.Contains(searchTextText, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntry>(list);
    }

    private void NoSearchDuplicateFile(object sender, RoutedEventArgs e)
    {
        SearchText.Text = "";
        SearchText.IsReadOnly = false;
        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntry>(_mainViewModel.DuplicateFilesEntries);
    }
}