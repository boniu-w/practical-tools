using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.DuplicateFiles.service;
using PracticalTools.DuplicateFiles.Wrapper;
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
    private readonly MainViewModel _mainViewModel;
    private DuplicateFilesService _duplicateFilesService;

    public MainWindow(MainViewModel mainViewModel, DuplicateFilesService duplicateFilesService)
    {
        InitializeComponent();

        // 从资源加载图标（单文件发布兼容）
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/icons8-cancer-48.png", UriKind.Absolute);
            this.Icon = BitmapFrame.Create(iconUri);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载窗口图标失败，使用默认图标");
        }

        DataContext = mainViewModel;
        _mainViewModel = mainViewModel;
        _duplicateFilesService = duplicateFilesService;
        SetMessageText(_message1);

        ScanButton.Click += SayHello;
    }

    /*****************************************************************************************************************************/

    // 用于停止扫描
    public CancellationTokenSource _cts;

    // 保存扫描结果
    private List<DuplicateFilesEntryWrapper> _scanResultDuplicateFileList;

    private static string _message0 = "";
    private string _message1 = "扫描文件夹下文件, 把重复的文件显示出来";

    /*****************************************************************************************************************************/

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

    /// <summary>
    /// 扫描多个文件夹
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ScanTheFolders(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        try
        {
            ScanButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait; // 设置鼠标为等待状态
            string scanTypeText = ScanType.Text;
            string[]? allowedExtensions;
            string errorMes;
            bool b = TryInitAllowedExtensions(scanTypeText, out allowedExtensions, out errorMes);
            if (!b)
            {
                MessageBox.Show(errorMes, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool isEmpty = string.IsNullOrWhiteSpace(_selectedFolder);
            string[] filePathes = isEmpty ? Array.Empty<string>() : _selectedFolder.Split(';');

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
            Log.Error(exception.Message);
            SetMessageText("扫描已取消");
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanProgress.Value = 0;
            _cts.Dispose();
            _cts = null;
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
        SearchText.IsReadOnly = false;
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

    /// <summary>
    /// 智能删除: 每组重复文件自动保留 1 个, 其余放入回收站。
    /// 如果勾选了文件, 只在勾选范围内处理; 否则处理当前扫描结果中的全部重复文件。
    /// </summary>
    /// <param name="sender">sender</param>
    /// <param name="e">e</param>
    private void SmartDeleteDuplicateFiles(object sender, RoutedEventArgs e)
    {
        List<DuplicateFilesEntryWrapper>? selectedList = _mainViewModel.FileList?.Where(e => e.IsSelected).ToList();
        List<DuplicateFilesEntryWrapper>? sourceList = selectedList is { Count: > 0 }
            ? selectedList
            : _mainViewModel.FileList?.ToList();

        if (sourceList == null || sourceList.Count == 0)
        {
            MessageBox.Show("当前没有可智能删除的文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        List<DuplicateFilesEntryWrapper> deleteList = sourceList
            .Where(e => !string.IsNullOrWhiteSpace(e.Entry.Md5Value))
            .GroupBy(e => e.Entry.Md5Value!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderBy(SmartKeepSortKey).Skip(1))
            .ToList();

        if (deleteList.Count == 0)
        {
            MessageBox.Show("没有发现可智能删除的重复文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"智能删除会在每组重复文件中保留 1 个文件，其余文件放入回收站。\n\n" +
            $"保留规则：优先保留创建时间最早、路径层级最浅的文件。\n" +
            $"本次将删除 {deleteList.Count} 个文件到回收站。\n\n" +
            $"确定继续吗？",
            "提示",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.OK)
        {
            ExecuteSmartDelete(deleteList);
        }
    }

    /// <summary>
    /// 执行智能删除: 将不需要保留的重复文件放入回收站, 并刷新列表。
    /// </summary>
    /// <param name="deleteList">要删除的文件列表</param>
    private void ExecuteSmartDelete(List<DuplicateFilesEntryWrapper> deleteList)
    {
        string[] filePaths = deleteList
            .Select(e => e.Entry.FilePath)
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Select(filePath => filePath!)
            .ToArray();
        _duplicateFilesService.MoveFileToRecycleBin(filePaths);

        HashSet<string> excludePaths = new HashSet<string>(filePaths, StringComparer.OrdinalIgnoreCase);

        // 筛选 FileList 中不包含在 excludePaths 的条目
        List<DuplicateFilesEntryWrapper> list = (_mainViewModel.FileList ?? new ObservableCollection<DuplicateFilesEntryWrapper>())
            .Where(entry => entry?.Entry.FilePath is { } filePath && !excludePaths.Contains(filePath))
            .ToList();

        _mainViewModel.FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(list);

        // 同步更新“不搜索”时使用的原始扫描结果
        _scanResultDuplicateFileList = (_scanResultDuplicateFileList ?? new List<DuplicateFilesEntryWrapper>())
            .Where(entry => entry?.Entry.FilePath is { } filePath && !excludePaths.Contains(filePath))
            .ToList();
    }

    /// <summary>
    /// 智能删除保留文件的排序依据。
    /// 排序越靠前越优先保留。
    /// </summary>
    /// <param name="wrapper">重复文件条目</param>
    /// <returns>排序键</returns>
    private static (DateTime CreateTime, int DirectoryDepth, int PathLength, string FileName) SmartKeepSortKey(
        DuplicateFilesEntryWrapper wrapper)
    {
        string? directory = Path.GetDirectoryName(wrapper.Entry.FilePath);
        int directoryDepth = string.IsNullOrWhiteSpace(directory)
            ? 0
            : directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;

        return (
            wrapper.Entry.FileCreateTime ?? DateTime.MaxValue,
            directoryDepth,
            wrapper.Entry.FilePath?.Length ?? int.MaxValue,
            wrapper.Entry.FileName ?? "");
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    /// <summary>
    /// 消息框
    /// </summary>
    /// <param name="message"></param>
    public void SetMessageText(string message)
    {
        MessageText.Text = message;
    }

    private void SayHello(object sender, RoutedEventArgs e) => Console.WriteLine("hello");

    private void ScanType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
        {
            bool isCustom = item.Content?.ToString() == "自定义";
            CustomColumn.Width = isCustom
                ? new GridLength(50, GridUnitType.Star)
                : new GridLength(0);
        }
    }

    private bool TryInitAllowedExtensions(string scanTypeText, out string[]? extensions, out string error)
    {
        error = string.Empty;
        extensions = null;

        string cusText = Cus.Text;

        if (scanTypeText == "自定义" && !string.IsNullOrWhiteSpace(cusText))
        {
            if (!FileUtil.IsValidFileExtension(cusText))
            {
                error = "请输入正确的文件名格式（例如：.txt）";
                return false;
            }

            extensions = new[] { cusText };
            return true;
        }

        extensions = scanTypeText switch
        {
            null or "" => FileUtil._nullExtensions,
            "所有类型" => FileUtil._defaultAllowedExtensions,
            "图片类" => FileUtil._phototExtensions,
            "音频类" => FileUtil._audioExtensions,
            "视频类" => FileUtil._videoExtensions,
            "文字类" => FileUtil._wordExtensions,
            "压缩类" => FileUtil._compressedFileExtensions,
            "编程语言类" => FileUtil._programExtensions,
            "无类型类" or "自定义" => FileUtil._nullExtensions,
            _ => FileUtil._defaultAllowedExtensions
        };

        return true;
    }

}