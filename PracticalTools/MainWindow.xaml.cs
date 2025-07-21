using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.DuplicateFiles.service;
using PracticalTools.DuplicateFiles.Wrapper;
using PracticalTools.FileManager.Model;
using PracticalTools.FileManager.Util;
using PracticalTools.GPG;
using PracticalTools.Sqlite;
using PracticalTools.util;
using PracticalTools.vo;
using Serilog;
using ColumnHeaderAttribute = PracticalTools.util.ColumnHeaderAttribute;
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
        SetupDataGridColumns();
        SetMessageText(_message1);

        // ScanButton.Click += ScanTheFolder;   // 测试使用事件绑定 button上没有明写 click
    }

    /*****************************************************************************************************************************/

    // 用于停止扫描
    public CancellationTokenSource _cts;

    // 保存扫描结果
    private List<DuplicateFilesEntryWrapper> _scanResultDuplicateFileList;

    private static string _message0 = "";
    private string _message1 = "扫描文件夹下文件, 把重复的文件显示出来";
    private string _message2 = "可管理所有文件";

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

    private string _selectedFolder = "";

    private void PickFile(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog();
        if (folderDialog.ShowDialog() == true)
        {
            if (!string.IsNullOrEmpty(_selectedFolder))
            {
                _selectedFolder += ";";
            }

            _selectedFolder += folderDialog.FolderName;
            TheFilePath.Text = _selectedFolder;
            TheFilePath.IsReadOnly = true;

            if (!string.IsNullOrEmpty(TheFilePath.Text))
            {
                PickFileButton.Content = "再选择一个文件夹";
            }
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
    /// 扫描单个文件夹
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ScanTheFolder(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
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
                    await ScanAll(allowedExtensions);
                }
            }
            else
            {
                ScanMessageOfStart();
                Log.Information($"开始扫描 [{theFilePath}] {scanTypeText}");
                Task task = Task.Run(() => Scan(theFilePath, _cts.Token, allowedExtensions));
                await ScanTaskMessageOfEnd(task);
            }
        }
        catch (Exception exception)
        {
            _mainViewModel.FileList?.Clear();
            MessageBox.Show("扫描已取消。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Log.Error("扫描已取消", exception.StackTrace);
            SetMessageText("扫描已取消");
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanProgress.Value = 0;
            _cts.Dispose();
            Mouse.OverrideCursor = null; // 恢复默认鼠标样式
        }
    }

    /// <summary>
    /// 扫描多个文件夹
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ScanTheFolders(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait; // 设置鼠标为等待状态
        string scanTypeText = ScanType.Text;
        string[]? allowedExtensions = InitAllowedExtensions(scanTypeText);

        _cts = new CancellationTokenSource();
        bool isEmpty = string.IsNullOrWhiteSpace(_selectedFolder);
        string[] filePathes = isEmpty ? Array.Empty<string>() : _selectedFolder.Split(';');

        try
        {
            if (filePathes.Length == 0)
            {
                MessageBoxResult messageBoxResult = MessageBox.Show("没有选择任何文件夹, 将扫描整个电脑",
                    "提示",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (messageBoxResult == MessageBoxResult.OK)
                {
                    await ScanAll(allowedExtensions);
                }
            }
            else
            {
                ScanMessageOfStart();
                Log.Information($"开始扫描 [{filePathes}] {scanTypeText}");
                Task task = Task.Run(() => Scan(filePathes, _cts.Token, allowedExtensions));
                await ScanTaskMessageOfEnd(task);
            }
        }
        catch (Exception exception)
        {
            _mainViewModel.FileList?.Clear();
            MessageBox.Show("扫描已取消。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Log.Error("扫描已取消", exception.StackTrace);
            SetMessageText("扫描已取消");
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanProgress.Value = 0;
            _cts.Dispose();
            Mouse.OverrideCursor = null; // 恢复默认鼠标样式
        }
    }

    private async Task ScanAll(string[]? allowedExtensions)
    {
        foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
        {
            if (driveInfo.IsReady)
            {
                ScanMessageOfStart();
                Log.Information($"开始全盘扫描");
                Task task = Task.Run(() => Scan(driveInfo.RootDirectory.FullName, _cts.Token, allowedExtensions));
                await ScanTaskMessageOfEnd(task);
            }
        }
    }

    private void ScanMessageOfStart()
    {
        SetMessageText($"扫描{_message0}开始");
        ScanProgress.Visibility = Visibility.Visible;
    }

    public async Task ScanTaskMessageOfEnd(Task task)
    {
        await task;
        if (task.Status == TaskStatus.RanToCompletion)
        {
            Log.Information("扫描完成");
            SetMessageText("扫描完成");
        }
        else if (task.Status == TaskStatus.Faulted)
        {
            // 任务执行失败，可以通过 task.Exception 获取异常
            Log.Error($"扫描失败 {task.Exception}");
            SetMessageText($"扫描失败 {task.Exception}");
        }
        else if (task.Status == TaskStatus.Canceled)
        {
            // 任务被取消
            Log.Information("扫描取消");
            SetMessageText("扫描取消");
        }
    }

    public string[]? InitAllowedExtensions(string scanTypeText)
    {
        if (string.IsNullOrEmpty(scanTypeText))
        {
            return FileUtil._defaultAllowedExtensions;
        }
        else
        {
            return scanTypeText switch
            {
                "所有类型" => FileUtil._defaultAllowedExtensions,
                "图片类" => FileUtil._phototExtensions,
                "音频类" => FileUtil._audioExtensions,
                "视频类" => FileUtil._videoExtensions,
                "文字类" => FileUtil._wordExtensions,
                "压缩类" => FileUtil._compressedFileExtensions,
                "编程语言类" => FileUtil._programExtensions,
                "无类型" => FileUtil._nullExtensions,
                _ => FileUtil._defaultAllowedExtensions
            };
        }
    }

    // private void Scan(string theFilePath)
    // {
    //     List<DuplicateFilesEntry> entries = _duplicateFilesService.GetAllFile(theFilePath);
    //     _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntry>(entries);
    // }

    public void Scan(string theFilePath, CancellationToken cancellationToken, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> list = GetDuplicateFiles(theFilePath, cancellationToken, allowedExtensions);
        _mainViewModel.FileList =
            new ObservableCollection<DuplicateFilesEntryWrapper>(list.Select(e => new DuplicateFilesEntryWrapper(e)));
        _scanResultDuplicateFileList = list.Select(e => new DuplicateFilesEntryWrapper(e)).ToList();
    }

    /// <summary>
    /// 可选择多个文件夹进行扫描
    /// </summary>
    /// <param name="theFilePaths"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="allowedExtensions"></param>
    public void Scan(string[] theFilePaths, CancellationToken cancellationToken, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> list = GetDuplicateFiles(theFilePaths, cancellationToken, allowedExtensions);
        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(list.Select(e => new DuplicateFilesEntryWrapper(e)));
        _scanResultDuplicateFileList = list.Select(e => new DuplicateFilesEntryWrapper(e)).ToList();
    }

    private List<DuplicateFilesEntry> GetDuplicateFiles(string path, CancellationToken token = default, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> files = _duplicateFilesService.GetAllFiles(path, token, allowedExtensions);
        _mainViewModel.HistoryFileList = new ObservableCollection<DuplicateFilesEntry>(files);

        List<DuplicateFilesEntry> list = files.GroupBy(f => f.Md5Value)
            .Where(f => f.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        return list;
    }

    /// <summary>
    /// 选择多个文件夹进行扫描
    /// </summary>
    /// <param name="paths"></param>
    /// <param name="token"></param>
    /// <param name="allowedExtensions"></param>
    /// <returns></returns>
    private List<DuplicateFilesEntry> GetDuplicateFiles(string[] paths, CancellationToken token = default, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> all = new List<DuplicateFilesEntry>();
        foreach (string path in paths)
        {
            List<DuplicateFilesEntry> files = _duplicateFilesService.GetAllFiles(path, token, allowedExtensions);
            all.AddRange(files);
        }

        _mainViewModel.HistoryFileList = new ObservableCollection<DuplicateFilesEntry>(all);
        List<DuplicateFilesEntry> list = all.GroupBy(f => f.Md5Value)
            .Where(f => f.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        return list;
    }

    private void UnPickFile(object sender, RoutedEventArgs e)
    {
        _selectedFolder = "";
        TheFilePath.Text = "";
        PickFileButton.Content = "选择一个文件夹";
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
        _mainViewModel.FileList =
            new ObservableCollection<DuplicateFilesEntryWrapper>(_scanResultDuplicateFileList ??=
                new List<DuplicateFilesEntryWrapper>());
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
        MessageBoxResult messageBox = MessageBox.Show("删除后将不可恢复, 确定删掉吗",
            "提示",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (messageBox == MessageBoxResult.OK)
        {
            List<DuplicateFilesEntryWrapper> selectedList = _mainViewModel.FileList?.Where(e => e.IsSelected).ToList();

            List<DuplicateFilesEntryWrapper> wrappers = selectedList.GroupBy(e => e.Entry.Md5Value)
                .Where(e => e.Count() > 1)
                .SelectMany(g => g)
                .ToList();

            if (wrappers.Count > 0)
            {
                MessageBoxResult messageBoxResult = MessageBox.Show("文件有相同的hash值, 确定都删掉吗",
                    "提示",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (messageBoxResult == MessageBoxResult.OK)
                {
                    ExecuteDeleteFiles(selectedList);
                }
            }
            else
            {
                ExecuteDeleteFiles(selectedList);
            }
        }
    }

    private void ExecuteDeleteFiles(List<DuplicateFilesEntryWrapper> selectedList)
    {
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

        List<DuplicateFilesEntryWrapper> wrappers = selectedList.GroupBy(e => e.Entry.Md5Value)
            .Where(e => e.Count() > 1)
            .SelectMany(g => g)
            .ToList();

        if (wrappers.Count > 0)
        {
            MessageBoxResult messageBoxResult = MessageBox.Show("文件有相同的hash值, 确定都删掉吗",
                "提示",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (messageBoxResult == MessageBoxResult.OK)
            {
                ExecuteMoveFileToRecycleBin(selectedList);
            }
        }
        else
        {
            ExecuteMoveFileToRecycleBin(selectedList);
        }
    }

    private void ExecuteMoveFileToRecycleBin(List<DuplicateFilesEntryWrapper> selectedList)
    {
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

    /// <summary>
    /// 测试变色
    /// </summary>
    /// <author>wg</author>
    /// <created>2025年4月29日</created>
    /// <modified>2025年4月29日</modified>
    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MyTabControl.SelectedItem is TabItem selectedItem && MyTabControl.SelectedItem == ScanResult)
        {
            // 走到这了, 但背景色没变
            // Console.WriteLine(222222);
            // MyTabControl.Background = Brushes.Red;
        }
        else
        {
            // MyTabControl.ClearValue(Control.BackgroundProperty);
        }
    }

    /// <summary>
    /// 自动添加列, 管用
    /// </summary>
    /// <author>wg</author>
    /// <created>2025年4月30日</created>
    /// <modified>2025年4月30日</modified>
    private void SetupDataGridColumns()
    {
        var properties = typeof(DuplicateFilesEntry).GetProperties();
        foreach (var prop in properties)
        {
            var attr = prop.GetCustomAttribute<ColumnHeaderAttribute>();
            if (attr == null)
                continue; // ❌ 没有注解，不添加这个列

            var column = new DataGridTextColumn
            {
                Header = attr.DisplayName,
                Binding = new Binding(prop.Name),
                Width = ColumnWidth(prop.Name)
            };
            MyDataGrid.Columns.Add(column);
        }

        // 设置数据源
        MyDataGrid.ItemsSource = new ObservableCollection<DuplicateFilesEntry>
        {
            new DuplicateFilesEntry { FileName = "test.txt", FileSize = 1 },
            new DuplicateFilesEntry { FileName = "hello.doc", FileSize = 1 }
        };
    }

    /// <summary>
    /// 设置列宽
    /// </summary>
    /// <param name="propertyName"></param>
    /// <returns></returns>
    private DataGridLength ColumnWidth(string propertyName)
    {
        return propertyName switch
        {
            "FileName" => new DataGridLength(2, DataGridLengthUnitType.Star),
            "FileSize" => new DataGridLength(1, DataGridLengthUnitType.Star),
            "Id" => new DataGridLength(80), // 固定像素宽度
            _ => new DataGridLength(1, DataGridLengthUnitType.Star),
        };
    }

    /// <summary>
    /// 消息框
    /// </summary>
    /// <param name="message"></param>
    public void SetMessageText(string message)
    {
        MessageText.Text = message;
    }

    private void AddNewItem(object sender, RoutedEventArgs e)
    {
        AddCardFormVisibility.Visibility = Visibility.Visible;
    }

    private void CancelNewItem(object sender, RoutedEventArgs e)
    {
        AddCardFormVisibility.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 程序将在x秒后退出
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Exit(object sender, RoutedEventArgs e)
    {
        Title = "5 秒后关闭";

        // 创建DispatcherTimer（WPF 专用定时器）
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (sender, e) =>
        {
            timer.Stop();
            // Close(); // 关闭当前窗口
            // 退出整个程序
            Application.Current.Shutdown();
        };
        timer.Start();
    }

    private async void OnEncryptClick(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = Path.GetFileNameWithoutExtension(openFileDialog.FileName) + ".gpg",
                    Filter = "GPG Files (*.gpg)|*.gpg|All Files (*.*)|*.*"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    EncryptButton.IsEnabled = false;
                    StatusText.Text = "正在加密...";

                    // 显示“处理中”状态，比如禁用按钮
                    EncryptButton.IsEnabled = false;

                    await Task.Run(() => { GpgHelper.EncryptFile(openFileDialog.FileName, saveFileDialog.FileName, "boniu-w@live.cn"); });

                    StatusText.Text = "加密完成";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加密失败: {ex.Message}";
        }
        finally
        {
            EncryptButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 解密
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void OnDecryptClick(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = Path.GetFileNameWithoutExtension(openFileDialog.FileName),
                    Filter = "All Files (*.*)|*.*"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    DecryptButton.IsEnabled = false;
                    StatusText.Text = "正在解密...";

                    // 显示“处理中”状态，比如禁用按钮
                    DecryptButton.IsEnabled = false;

                    await Task.Run(() => { GpgHelper.DecryptFile(openFileDialog.FileName, saveFileDialog.FileName); });

                    StatusText.Text = "解密完成";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"解密失败: {ex.Message}";
        }
        finally
        {
            DecryptButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 进度条真管用
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public async void TestImport(object sender, RoutedEventArgs e)
    {
        DataTable data = FileHelper.ImportExcelFile("Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*");
        if (data == null || data.Rows.Count == 0)
        {
            MessageBox.Show("导入数据为空！");
            return;
        }

        DataColumnCollection columns = data.Columns;
        List<MyFile> myFiles = DataTableMapper.MapToMyFileList(data);
        MyFileImport.Text = myFiles.Count.ToString();
        int total = myFiles.Count;
        if (total == 0)
        {
            MessageBox.Show("未解析到任何有效数据！");
            return;
        }

        if (myFiles.Count > 0)
        {
            Mouse.OverrideCursor = Cursors.Wait; // 设置鼠标为等待状态
            MyFileImportProgressBar.Minimum = 0;
            MyFileImportProgressBar.Maximum = total;
            MyFileImportProgressBar.Value = 0;
            MyFileImportProgressBar.Visibility = Visibility.Visible;
            Progress<int> progress = new Progress<int>(value =>
            {
                MyFileImportProgressBar.Value = value;
                // 你可以更新进度条，比如 progressBar.Value = value;
            });

            await Task.Run(() =>
            {
                for (int i = 0; i < myFiles.Count; i++)
                {
                    Application.Current.Dispatcher.Invoke(() => { _mainViewModel.AddMyFile(myFiles[i]); });

                    ((IProgress<int>)progress).Report(i + 1);
                }
            });

            MyFileImportProgressBar.Visibility = Visibility.Hidden;
            MessageBox.Show($"导入完成, 共添加 {total} 条数据");
            Mouse.OverrideCursor = null; // 恢复默认鼠标样式
        }
    }
}