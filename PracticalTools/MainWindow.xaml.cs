using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.DuplicateFiles.service;
using PracticalTools.DuplicateFiles.Wrapper;
using PracticalTools.Sqlite;
using PracticalTools.util;
using PracticalTools.vo;
using Serilog;
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

    // 用于停止扫描
    private CancellationTokenSource _cts;

    // 保存扫描结果
    private List<DuplicateFilesEntryWrapper> _scanResultDuplicateFileList;
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

    // private void ScanTheFolder(object sender, RoutedEventArgs e)
    // {
    //     string theFilePath = "";
    //     if (TheFilePath.Text.Length == 0)
    //     {
    //         MessageBoxResult messageBoxResult = MessageBox.Show("没有选择任何文件夹, 将扫描整个电脑",
    //             "提示",
    //             MessageBoxButton.OKCancel,
    //             MessageBoxImage.Question);
    //         if (messageBoxResult == MessageBoxResult.OK)
    //         {
    //             foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
    //             {
    //                 if (driveInfo.IsReady)
    //                 {
    //                     // Scan(driveInfo.RootDirectory.FullName);
    //                 }
    //             }
    //         }
    //     }
    //     else
    //     {
    //         theFilePath = TheFilePath.Text;
    //         Scan(theFilePath);
    //     }
    // }

    /// <summary>
    /// 可停止扫描
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ScanTheFolder(object sender, RoutedEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.Wait; // 设置鼠标为等待状态
        string scanTypeText = ScanType.Text;
        string[]? allowedExtensions = InitAllowedExtensions(scanTypeText);

        _cts = new CancellationTokenSource();
        string? theFilePath = TheFilePath.Text?.Trim();

        try
        {
            if (string.IsNullOrEmpty(theFilePath))
            {
                MessageBoxResult messageBoxResult = MessageBox.Show("没有选择任何文件夹, 将扫描整个电脑",
                    "提示",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (messageBoxResult == MessageBoxResult.OK)
                {
                    foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
                    {
                        if (driveInfo.IsReady)
                        {
                            Log.Information($"开始全盘扫描");
                            Task task = Task.Run(() => Scan(driveInfo.RootDirectory.FullName, _cts.Token, allowedExtensions));
                            await ScanTaskMessage(task);
                        }
                    }
                }
            }
            else
            {
                Log.Information($"开始扫描 [{theFilePath}] {scanTypeText}");
                Task task = Task.Run(() => Scan(theFilePath, _cts.Token, allowedExtensions));
                await ScanTaskMessage(task);
            }
        }
        catch (Exception exception)
        {
            _mainViewModel.FileList?.Clear();
            MessageBox.Show("扫描已取消。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Log.Error("扫描已取消");
        }
        finally
        {
            _cts.Dispose();
            Mouse.OverrideCursor = null; // 恢复默认鼠标样式
        }
    }

    private async Task ScanTaskMessage(Task task)
    {
        await task;
        if (task.Status == TaskStatus.RanToCompletion)
        {
            Log.Information("扫描完成");
        }
        else if (task.Status == TaskStatus.Faulted)
        {
            // 任务执行失败，可以通过 task.Exception 获取异常
            Log.Error($"扫描失败 {task.Exception}");
        }
        else if (task.Status == TaskStatus.Canceled)
        {
            // 任务被取消
            Log.Information("扫描取消");
        }
    }

    private string[]? InitAllowedExtensions(string scanTypeText)
    {
        string[]? allowedExtensions;
        if (string.IsNullOrEmpty(scanTypeText))
        {
            allowedExtensions = FileUtil._defaultAllowedExtensions;
        }
        else
        {
            switch (scanTypeText)
            {
                case "所有类型":
                    allowedExtensions = FileUtil._defaultAllowedExtensions;
                    break;
                case "图片类":
                    allowedExtensions = FileUtil._phototExtensions;
                    break;
                case "音频类":
                    allowedExtensions = FileUtil._audioExtensions;
                    break;
                case "视频类":
                    allowedExtensions = FileUtil._videoExtensions;
                    break;
                case "文字类":
                    allowedExtensions = FileUtil._wordExtensions;
                    break;
                case "压缩类":
                    allowedExtensions = FileUtil._compressedFileExtensions;
                    break;
                case "程序类":
                    allowedExtensions = FileUtil._programExtensions;
                    break;
                case "无类型":
                    allowedExtensions = FileUtil._nullExtensions;
                    break;
                default:
                    allowedExtensions = FileUtil._defaultAllowedExtensions;
                    break;
            }
        }

        return allowedExtensions;
    }

    // private void Scan(string theFilePath)
    // {
    //     List<DuplicateFilesEntry> entries = _duplicateFilesService.GetAllFile(theFilePath);
    //     _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntry>(entries);
    // }

    private void Scan(string theFilePath, CancellationToken cancellationToken, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> list = GetDuplicateFiles(theFilePath, cancellationToken, allowedExtensions);
        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(list.Select(e => new DuplicateFilesEntryWrapper(e)));
        _scanResultDuplicateFileList = list.Select(e => new DuplicateFilesEntryWrapper(e)).ToList();
    }

    private List<DuplicateFilesEntry> GetDuplicateFiles(string path, CancellationToken token = default, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> files = _duplicateFilesService.GetAllFiles(path, token, allowedExtensions);
        _mainViewModel.HistoryFileList = new ObservableCollection<DuplicateFilesEntry>(files);

        List<DuplicateFilesEntry> list = files.GroupBy(f => f.HashValue)
            .Where(f => f.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        return list;
    }

    private void UnPickFile(object sender, RoutedEventArgs e)
    {
        TheFilePath.Text = "";
        TheFilePath.IsReadOnly = false;
    }

    private void StopScanTheFolder(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// 开始扫描文件夹, 找出重复文件
    /// </summary>
    /// <param name="e">e</param>
    /// <returns>RETURN_DESC</returns>
    /// <author>wg</author>
    /// <date>2025年4月28日</date>
    private void SearchDuplicateFile(object sender, RoutedEventArgs e)
    {
        SearchText.IsReadOnly = true;
        string searchTextText = SearchText.Text?.Trim();
        if (string.IsNullOrEmpty(searchTextText))
        {
            return;
        }

        List<DuplicateFilesEntryWrapper> list = _mainViewModel.FileList
            .Where(e => e.Entry.FileName.Contains(searchTextText, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(list);
    }

    /// <summary>
    /// 不搜索
    /// </summary>
    /// <param name="e">e</param>
    /// <returns>RETURN_DESC</returns>
    /// <author>wg</author>
    /// <date>2025年4月28日</date>
    private void NoSearchDuplicateFile(object sender, RoutedEventArgs e)
    {
        SearchText.Text = "";
        SearchText.IsReadOnly = false;
        // _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(_mainViewModel.DuplicateFilesEntries.Select(e => new DuplicateFilesEntryWrapper(e)));
        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(_scanResultDuplicateFileList ??= new List<DuplicateFilesEntryWrapper>());
    }

    /// <summary>
    /// 彻底删除文件
    /// </summary>
    /// <param name="e">e</param>
    /// <returns>RETURN_DESC</returns>
    /// <author>wg</author>
    /// <date>2025年4月28日</date>
    private void DeleteFiles(object sender, RoutedEventArgs e)
    {
        List<DuplicateFilesEntryWrapper> selectedList = _mainViewModel.FileList?.Where(e => e.IsSelected).ToList();

        string[] filePaths = selectedList.Select(e => e.Entry.FilePath).ToArray();
        _duplicateFilesService.DeleteFiles(filePaths);

        HashSet<string> excludePaths = new HashSet<string>(filePaths, StringComparer.OrdinalIgnoreCase);

        // 筛选 FileList 中不包含在 excludePaths 的条目
        List<DuplicateFilesEntryWrapper> list = _mainViewModel.FileList?
            .Where(entry => !excludePaths.Contains(entry?.Entry.FilePath))
            .ToList();

        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(list);
    }

    /// <summary>
    /// 放入回收站
    /// </summary>
    /// <param name="e">e</param>
    /// <returns>RETURN_DESC</returns>
    /// <author>wg</author>
    /// <date>2025年4月28日</date>
    private void MoveFileToRecycleBin(object sender, RoutedEventArgs e)
    {
        List<DuplicateFilesEntryWrapper> selectedList = _mainViewModel.FileList?.Where(e => e.IsSelected).ToList();

        string[] filePaths = selectedList.Select(e => e.Entry.FilePath).ToArray();
        _duplicateFilesService.MoveFileToRecycleBin(filePaths);

        HashSet<string> excludePaths = new HashSet<string>(filePaths, StringComparer.OrdinalIgnoreCase);

        // 筛选 FileList 中不包含在 excludePaths 的条目
        List<DuplicateFilesEntryWrapper> list = _mainViewModel.FileList?
            .Where(entry => !excludePaths.Contains(entry?.Entry.FilePath))
            .ToList();

        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(list);
    }


    private void GetSelectedAll(object sender, RoutedEventArgs e)
    {
        List<DuplicateFilesEntryWrapper> list = _mainViewModel.FileList.Where(e => e.IsSelected).ToList();
        foreach (var item in list)
        {
            Console.WriteLine(item);
        }
    }
}