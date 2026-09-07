using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.DuplicateFiles.Wrapper;
using PracticalTools.FileManager.Model;
using PracticalTools.FileManager.Service;
using PracticalTools.GPG;
using PracticalTools.Sqlite;
using PracticalTools.util;
using PracticalTools.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OfficeOpenXml;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PracticalTools.vo;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /**************************************************************************************************************/

    public readonly MyDbContext _myDbContext;
    // private readonly MainWindow _mainWindow;

    public MainViewModel(MyDbContext myDbContext)
    {
        // _mainWindow = mainWindow;
        _myDbContext = myDbContext;
        RefreshCommand = new RelayCommand<Object>(RefreshBlackList);
        RefreshBlackList<Object>(null);
        InitMyFileList();
        InitializePaging();

        NextCommand = new RelayCommand<Object>(_ =>
        {
            PagedFileList.NextPage();
            UpdatePageInfo();
        });
        PreviousCommand = new RelayCommand<Object>(_ =>
        {
            PagedFileList.PreviousPage();
            UpdatePageInfo();
        });
        FirstCommand = new RelayCommand<Object>(_ =>
        {
            PagedFileList.FirstPage();
            UpdatePageInfo();
        });
        LastCommand = new RelayCommand<Object>(_ =>
        {
            PagedFileList.LastPage();
            UpdatePageInfo();
        });


        // 测试 使用command 方式, [必须放这, 不能放到 MainWindow 里]
        // ScanCommand = new RelayCommand<string>(
        //     execute: ScanFolder,
        //     canExecute: path => !string.IsNullOrEmpty(path) && !IsScanning // 只有路径非空才允许执行
        // );

        LoadGpgFileList();
        WrapperSelectedChangedEvent();

        DeleteSelectedGpgFilesCommand = new RelayCommand<object>(DeleteSelectedGpgFiles);
        ExportSelectedGpgFilesCommand = new RelayCommand<object>(ExportPublic);
        ImportGpgFilesCommand = new RelayCommand<object>(ImportGpgFiles);
        BackupSelectedGpgFilesCommand = new RelayCommand<object>(BackupSelectedGpgFiles);
        BackupAllGpgFilesCommand = new RelayCommand<object>(BackupAllGpgFiles);
        RestoreGpgBackupCommand = new RelayCommand<object>(RestoreGpgBackup);
    }

    // private bool _isScanning;
    //
    // public bool IsScanning
    // {
    //     get => _isScanning;
    //     set
    //     {
    //         _isScanning = value;
    //         OnPropertyChanged(nameof(IsScanning));
    //     }
    // }

    // 测试 使用command 方式, [必须放这, 不能放到 MainWindow 里]
    public ICommand ScanCommand { get; }

    // 保存数据库数据
    public List<DuplicateFilesEntry> DuplicateFilesEntries { get; set; }

    public ICommand RefreshCommand { get; }
    public ICommand DeleteSelectedGpgFilesCommand { get; }
    public ICommand ExportSelectedGpgFilesCommand { get; }
    public ICommand ImportGpgFilesCommand { get; }
    public ICommand BackupSelectedGpgFilesCommand { get; }
    public ICommand BackupAllGpgFilesCommand { get; }
    public ICommand RestoreGpgBackupCommand { get; }

    // 重复的文件
    private ObservableCollection<DuplicateFilesEntryWrapper>? _fileList;

    public ObservableCollection<DuplicateFilesEntryWrapper>? FileList
    {
        get => _fileList;
        set
        {
            if (_fileList != value)
            {
                // 先解绑旧的
                if (_fileList != null)
                {
                    // foreach (var item in _fileList)
                    //     item.PropertyChanged -= FileItem_PropertyChanged;
                }

                _fileList = value;

                // 绑定新的
                if (_fileList != null)
                {
                    // foreach (var item in _fileList)
                    // item.PropertyChanged += FileItem_PropertyChanged;
                }

                OnPropertyChanged(nameof(FileList));
                // UpdateIsAllSelected(); // 顺便刷新一下全选状态
            }
        }
    }

    private ObservableCollection<GpgFileEntryWrapper>? _gpgFileWrapperList;

    public ObservableCollection<GpgFileEntryWrapper>? GpgFileWrapperList
    {
        get => _gpgFileWrapperList;
        set
        {
            if (_gpgFileWrapperList != value)
            {
                _gpgFileWrapperList = value;
                OnPropertyChanged(nameof(GpgFileWrapperList));
                UpdateIsGpgAllSelected();
            }
        }
    }

    private bool? _isGpgAllSelected = false;

    public bool? IsGpgAllSelected
    {
        get => _isGpgAllSelected;
        set
        {
            if (_isGpgAllSelected != value)
            {
                _isGpgAllSelected = value;
                OnPropertyChanged();

                if (GpgFileWrapperList != null && value.HasValue)
                {
                    foreach (GpgFileEntryWrapper item in GpgFileWrapperList)
                        item.IsSelected = value.Value;
                }
            }
        }
    }

    private void UpdateIsGpgAllSelected()
    {
        if (GpgFileWrapperList == null || GpgFileWrapperList.Count == 0)
        {
            IsGpgAllSelected = false;
            return;
        }
        int count = GpgFileWrapperList.Count(x => x.IsSelected);
        if (count == 0)
            IsGpgAllSelected = false;
        else if (count == GpgFileWrapperList.Count)
            IsGpgAllSelected = true;
        else
            IsGpgAllSelected = null;
    }

    private ObservableCollection<BlacklistEntry>? _blacklists;

    public ObservableCollection<BlacklistEntry>? Blacklists
    {
        get => _blacklists;
        set
        {
            _blacklists = value;
            OnPropertyChanged(nameof(Blacklists));
        }
    }

    // 扫描历史文件
    private ObservableCollection<DuplicateFilesEntry>? _historyFileList;

    public ObservableCollection<DuplicateFilesEntry>? HistoryFileList
    {
        get => _historyFileList;
        set
        {
            _historyFileList = value;
            OnPropertyChanged();
        }
    }

    // 分页显示扫描历史文件
    private ObservableCollection<DuplicateFilesEntryWrapper> _pageFileList;

    public ObservableCollection<DuplicateFilesEntryWrapper> PageFileList
    {
        get => _pageFileList;
        set
        {
            _pageFileList = value;
            OnPropertyChanged(nameof(PageFileList));
        }
    }

    // private bool _isSelected;
    //
    // public bool IsSelected
    // {
    //     get => _isSelected;
    //     set
    //     {
    //         if (_isSelected != value)
    //         {
    //             _isSelected = value;
    //             OnPropertyChanged(nameof(IsSelected));
    //         }
    //     }
    // }

    // private bool? _isAllSelected = false;
    //
    // public bool? IsAllSelected
    // {
    //     get => _isAllSelected;
    //     set
    //     {
    //         if (_isAllSelected != value)
    //         {
    //             _isAllSelected = value;
    //             if (FileList != null && value.HasValue)
    //             {
    //                 foreach (var item in FileList)
    //                 {
    //                     item.IsSelected = value.Value;
    //                 }
    //             }
    //
    //             OnPropertyChanged(nameof(IsAllSelected));
    //         }
    //     }
    // }

    private UserControl _currentPage;

    public UserControl CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage != value)
            {
                _currentPage = value;
                OnPropertyChanged();
            }
        }
    }

    /**************************************************************************************************************/

    /// <summary>
    /// 测试 使用command 方式, [循环依赖了, 如果没有的话, 应该是能用的]
    /// </summary>
    /// <param name="path"></param>
    // public async void ScanFolder(string path)
    // {
    //     Console.WriteLine(path);
    //     _isScanning = true;
    //     _mainWindow.ScanButton.IsEnabled = false;
    //     Mouse.OverrideCursor = Cursors.Wait; // 设置鼠标为等待状态
    //     string scanTypeText = _mainWindow.ScanType.Text;
    //     string[]? allowedExtensions = _mainWindow.InitAllowedExtensions(scanTypeText);
    //
    //     _mainWindow._cts = new CancellationTokenSource();
    //     string? theFilePath = _mainWindow.TheFilePath.Text?.Trim();
    //
    //     try
    //     {
    //         if (string.IsNullOrEmpty(theFilePath))
    //         {
    //             MessageBoxResult messageBoxResult = MessageBox.Show("没有选择任何文件夹, 将扫描整个电脑",
    //                 "提示",
    //                 MessageBoxButton.OKCancel,
    //                 MessageBoxImage.Question);
    //             if (messageBoxResult == MessageBoxResult.OK)
    //             {
    //                 foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
    //                 {
    //                     if (driveInfo.IsReady)
    //                     {
    //                         _mainWindow.SetMessageText($"扫描开始");
    //                         Log.Information($"开始全盘扫描");
    //                         Task task = Task.Run(() => _mainWindow.Scan(driveInfo.RootDirectory.FullName, _mainWindow._cts.Token, allowedExtensions));
    //                         await _mainWindow.ScanTaskMessage(task);
    //                     }
    //                 }
    //             }
    //         }
    //         else
    //         {
    //             _mainWindow.SetMessageText($"扫描开始");
    //             Log.Information($"开始扫描 [{theFilePath}] {scanTypeText}");
    //             Task task = Task.Run(() => _mainWindow.Scan(theFilePath, _mainWindow._cts.Token, allowedExtensions));
    //             await _mainWindow.ScanTaskMessage(task);
    //         }
    //     }
    //     catch (Exception exception)
    //     {
    //         FileList?.Clear();
    //         MessageBox.Show("扫描已取消。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    //         Log.Error("扫描已取消");
    //         _mainWindow.SetMessageText("扫描时发生异常");
    //     }
    //     finally
    //     {
    //         _isScanning = false;
    //         _mainWindow.ScanButton.IsEnabled = true;
    //         _mainWindow._cts.Dispose();
    //         Mouse.OverrideCursor = null; // 恢复默认鼠标样式
    //     }
    // }

    // private void FileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    // {
    //     if (e.PropertyName == nameof(DuplicateFilesEntryWrapper.IsSelected))
    //     {
    //         UpdateIsAllSelected();
    //     }
    // }

    // private void UpdateIsAllSelected()
    // {
    //     if (FileList == null || FileList.Count == 0)
    //     {
    //         IsAllSelected = false;
    //         return;
    //     }
    //
    //     var selectedCount = FileList.Count(x => x.IsSelected);
    //     if (selectedCount == 0)
    //         IsAllSelected = false;
    //     else if (selectedCount == FileList.Count)
    //         IsAllSelected = true;
    //     else
    //         IsAllSelected = null; // 部分选中
    // }
    private void RefreshBlackList<T>(T obj)
    {
        LoadBlackLists();
    }

    private void LoadBlackLists()
    {
        try
        {
            // DuplicateFilesEntries = _myDbContext.DuplicateFiles.OrderByDescending(e => e.FileCreateTime).ToList();

            // 直接赋值，不用管 PropertyChanged
            // FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(
            //     DuplicateFilesEntries.Select(e => new DuplicateFilesEntryWrapper(e))
            // );

            Blacklists = new ObservableCollection<BlacklistEntry>(
                _myDbContext.Blacklist.OrderByDescending(e => e.UpdateTime)
            );
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message);
            throw;
        }
    }

    public void Navigate(string? pageName)
    {
        switch (pageName)
        {
            case "首页":
                CurrentPage = new HomePage();
                break;
            default:
                CurrentPage = new HomePage();
                break;
        }
    }

    public void AddMyFile(MyFile myFile)
    {
        List<MyFile> myFiles = _myDbContext.MyFile.ToList();
        List<string?> md5List = myFiles.Select(e => e.Md5Value).ToList();
        string? md5Value = myFile.Md5Value;
        if (md5List.Contains(md5Value))
        {
            return;
        }

        MyFileService myFileService = new MyFileService();
        myFileService.Add(myFile, _myDbContext);
    }

    public void InitMyFileList()
    {
        AllMyFiles =
            new ObservableCollection<MyFile>(_myDbContext.MyFile
                .OrderByDescending(e => e.UpdateTime)
                .ToList()
            );
    }

    private string _statusMessage;

    public string StatusMessage
    {
        get { return _statusMessage; }
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public void InitializePaging()
    {
        PagedFileList = new PagedList<MyFile>
        {
            SourceList = AllMyFiles,
            PageSize = 100
        };
        PagedFileList.Refresh();

        UpdatePageInfo();
    }

    private string _pageInfoText;

    public string PageInfoText
    {
        get => _pageInfoText;
        set
        {
            if (_pageInfoText != value)
            {
                _pageInfoText = value;
                OnPropertyChanged();
            }
        }
    }

    public void UpdatePageInfo()
    {
        PageInfoText = $"第 {PagedFileList.CurrentPage} 页 / 共 {PagedFileList.PageCount} 页";
    }

    public ObservableCollection<MyFile> AllMyFiles { get; set; } = new();
    public PagedList<MyFile> PagedFileList { get; set; }

    private bool? _isAllSelected = false;

    public bool? IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (_isAllSelected != value)
            {
                _isAllSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand FirstCommand { get; }
    public ICommand LastCommand { get; }

    public void UpdateIsAllSelectedForCurrentPageOnly()
    {
        if (PagedFileList.All(f => f.IsSelected))
            IsAllSelected = true;
        else if (PagedFileList.All(f => !f.IsSelected))
            IsAllSelected = false;
        else
            IsAllSelected = null; // 你可以不处理这项
    }

    public void LoadGpgFileList()
    {
        try
        {
            GpgFileWrapperList = new ObservableCollection<GpgFileEntryWrapper>(
                _myDbContext.GpgFile
                    .OrderByDescending(e => e.UpdateTime)
                    .Select(e => new GpgFileEntryWrapper(e)));
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message);
            throw;
        }
    }

    private void DeleteSelectedGpgFiles(object obj)
    {
        if (GpgFileWrapperList == null) return;

        List<GpgFileEntryWrapper> selected = GpgFileWrapperList.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选要删除的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"确定要删除选中的 {selected.Count} 个密钥吗？\n此操作不可撤销。",
            "确认批量删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            GpgFileService service = new GpgFileService(_myDbContext);
            foreach (GpgFileEntryWrapper wrapper in selected)
            {
                service.Delete(wrapper.GpgFile.Id);
                GpgFileWrapperList.Remove(wrapper);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show($"删除失败: {e.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 导出
    /// </summary>
    /// <param name="obj"></param>
    private void ExportSelectedGpgFiles(object obj)
    {
        if (GpgFileWrapperList == null) return;

        List<GpgFileEntryWrapper> selected = GpgFileWrapperList.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选要导出的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Title = "导出密钥对",
            FileName = $"GpgKeys_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            Filter = "ZIP 压缩包 (*.zip)|*.zip|All Files (*.*)|*.*",
            DefaultExt = ".zip"
        };

        if (saveFileDialog.ShowDialog() != true) return;
        string zipPath = saveFileDialog.FileName;

        try
        {
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using (FileStream zipStream = new FileStream(zipPath, FileMode.Create))
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // 1. 生成 Excel 并写入 ZIP
                using (ExcelPackage excelPackage = new ExcelPackage())
                {
                    ExcelWorksheet worksheet = excelPackage.Workbook.Worksheets.Add("GPG密钥");

                    string[] headers =
                    {
                        "身份", "别名", "密钥长度", "有效期开始于", "有效期截至于",
                        "有效时长(秒)", "私钥文件名", "公钥文件名", "说明", "创建时间", "更新时间"
                    };

                    for (int col = 0; col < headers.Length; col++)
                    {
                        ExcelRange cell = worksheet.Cells[1, col + 1];
                        cell.Value = headers[col];
                        cell.Style.Font.Bold = true;
                    }

                    for (int row = 0; row < selected.Count; row++)
                    {
                        GpgFile gpg = selected[row].GpgFile;
                        int r = row + 2;

                        worksheet.Cells[r, 1].Value = gpg.Identity;
                        worksheet.Cells[r, 2].Value = gpg.IdentityName;
                        worksheet.Cells[r, 3].Value = gpg.KeySize;
                        worksheet.Cells[r, 4].Value = gpg.ValidDurationBeginsOn?.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cells[r, 5].Value = gpg.ValidDurationEndsOn?.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cells[r, 6].Value = gpg.ValidDurationSeconds;
                        worksheet.Cells[r, 7].Value = gpg.FileNamePrivate;
                        worksheet.Cells[r, 8].Value = gpg.FileNamePublic;
                        worksheet.Cells[r, 9].Value = gpg.Description;
                        worksheet.Cells[r, 10].Value = gpg.CreateTime?.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cells[r, 11].Value = gpg.UpdateTime?.ToString("yyyy-MM-dd HH:mm:ss");
                    }

                    worksheet.Cells.AutoFitColumns();

                    ZipArchiveEntry excelEntry = archive.CreateEntry("GPG密钥汇总.xlsx");
                    using (Stream entryStream = excelEntry.Open())
                    {
                        excelPackage.SaveAs(entryStream);
                    }
                }

                // 2. 将公钥和私钥 .asc 文件写入 ZIP（以身份作为文件名）
                int ascCount = 0;
                Dictionary<string, int> usedNames = new Dictionary<string, int>();
                foreach (GpgFileEntryWrapper wrapper in selected)
                {
                    GpgFile gpg = wrapper.GpgFile;
                    string safeName = SanitizeFileName(gpg.Identity);

                    if (!string.IsNullOrEmpty(gpg.PublicKey))
                    {
                        string entryName = GetUniqueEntryName(usedNames, safeName + "_公钥", ".asc");
                        ZipArchiveEntry pubEntry = archive.CreateEntry(entryName);
                        using (Stream entryStream = pubEntry.Open())
                        using (StreamWriter writer = new StreamWriter(entryStream, Encoding.UTF8))
                        {
                            writer.Write(gpg.PublicKey);
                        }
                        ascCount++;
                    }

                    if (!string.IsNullOrEmpty(gpg.SecretKey))
                    {
                        string entryName = GetUniqueEntryName(usedNames, safeName + "_私钥", ".asc");
                        ZipArchiveEntry secEntry = archive.CreateEntry(entryName);
                        using (Stream entryStream = secEntry.Open())
                        using (StreamWriter writer = new StreamWriter(entryStream, Encoding.UTF8))
                        {
                            writer.Write(gpg.SecretKey);
                        }
                        ascCount++;
                    }
                }

                MessageBox.Show(
                    $"已导出 {selected.Count} 个密钥对到:\n{zipPath}\n\n内含: GPG密钥汇总.xlsx + {ascCount} 个 .asc 文件",
                    "导出成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show($"导出失败: {e.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPublic(object obj)
    {
        if (GpgFileWrapperList == null) return;

        List<GpgFileEntryWrapper> selected = GpgFileWrapperList.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选要导出的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Title = "导出公钥",
            FileName = $"GpgPublicKeys_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            Filter = "ZIP 压缩包 (*.zip)|*.zip|All Files (*.*)|*.*",
            DefaultExt = ".zip"
        };

        if (saveFileDialog.ShowDialog() != true) return;
        string zipPath = saveFileDialog.FileName;

        try
        {
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using (FileStream zipStream = new FileStream(zipPath, FileMode.Create))
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // 1. 生成 Excel 并写入 ZIP
                using (ExcelPackage excelPackage = new ExcelPackage())
                {
                    ExcelWorksheet worksheet = excelPackage.Workbook.Worksheets.Add("GPG公钥");

                    string[] headers =
                    {
                        "身份", "别名", "密钥长度", "有效期开始于", "有效期截至于",
                        "有效时长(秒)", "公钥文件名", "说明", "创建时间", "更新时间"
                    };

                    for (int col = 0; col < headers.Length; col++)
                    {
                        ExcelRange cell = worksheet.Cells[1, col + 1];
                        cell.Value = headers[col];
                        cell.Style.Font.Bold = true;
                    }

                    for (int row = 0; row < selected.Count; row++)
                    {
                        GpgFile gpg = selected[row].GpgFile;
                        int r = row + 2;

                        worksheet.Cells[r, 1].Value = gpg.Identity;
                        worksheet.Cells[r, 2].Value = gpg.IdentityName;
                        worksheet.Cells[r, 3].Value = gpg.KeySize;
                        worksheet.Cells[r, 4].Value = gpg.ValidDurationBeginsOn?.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cells[r, 5].Value = gpg.ValidDurationEndsOn?.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cells[r, 6].Value = gpg.ValidDurationSeconds;
                        worksheet.Cells[r, 7].Value = gpg.FileNamePublic;
                        worksheet.Cells[r, 8].Value = gpg.Description;
                        worksheet.Cells[r, 9].Value = gpg.CreateTime?.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cells[r, 10].Value = gpg.UpdateTime?.ToString("yyyy-MM-dd HH:mm:ss");
                    }

                    worksheet.Cells.AutoFitColumns();

                    // 将 Excel 写入 ZIP
                    ZipArchiveEntry excelEntry = archive.CreateEntry("GPG公钥汇总.xlsx");
                    using (Stream entryStream = excelEntry.Open())
                    {
                        excelPackage.SaveAs(entryStream);
                    }
                }

                // 2. 将公钥 .asc 文件写入 ZIP（以身份作为文件名）
                int ascCount = 0;
                Dictionary<string, int> usedNames = new Dictionary<string, int>();
                foreach (GpgFileEntryWrapper wrapper in selected)
                {
                    GpgFile gpg = wrapper.GpgFile;
                    if (!string.IsNullOrEmpty(gpg.PublicKey))
                    {
                        string safeName = SanitizeFileName(gpg.Identity);
                        string entryName = GetUniqueEntryName(usedNames, safeName, ".asc");
                        ZipArchiveEntry ascEntry = archive.CreateEntry(entryName);
                        using (Stream entryStream = ascEntry.Open())
                        using (StreamWriter writer = new StreamWriter(entryStream, Encoding.UTF8))
                        {
                            writer.Write(gpg.PublicKey);
                        }
                        ascCount++;
                    }
                }

                MessageBox.Show(
                    $"已导出 {selected.Count} 个公钥到:\n{zipPath}\n\n内含: GPG公钥汇总.xlsx + {ascCount} 个 .asc 文件",
                    "导出成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                GpgStatusMessage = $"已导出 {selected.Count} 个公钥到: {zipPath}";
            }
        }
        catch (Exception e)
        {
            MessageBox.Show($"导出失败: {e.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 导入
    /// </summary>
    /// <param name="obj"></param>
    private void ImportGpgFiles(object obj)
    {
        ImportGpgWindow window = new ImportGpgWindow();
        window.Owner = Application.Current.MainWindow;
        if (window.ShowDialog() != true) return;

        try
        {
            string publicKey = File.ReadAllText(window.PublicKeyPath);
            string fileName = Path.GetFileName(window.PublicKeyPath);

            // 尝试从公钥内容中提取身份
            string identity = ExtractIdentityFromPublicKey(publicKey);
            if (string.IsNullOrEmpty(identity))
                identity = Path.GetFileNameWithoutExtension(window.PublicKeyPath);

            string alias = string.IsNullOrEmpty(window.Alias)
                ? identity
                : window.Alias;

            // 检查是否已存在
            GpgFile? existing = _myDbContext.GpgFile.FirstOrDefault(f =>
                f.Identity == identity || f.FileNamePublic == fileName);
            if (existing != null)
            {
                MessageBox.Show($"公钥 \"{identity}\" 已存在 (ID: {existing.Id})，无法重复导入。",
                                "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GpgFileService service = new GpgFileService(_myDbContext);
            GpgFile gpgFile = service.GenerateGpgFile(
                identity, "",
                "", publicKey, // 无私钥
                DateTime.Now, null, "",
                0, 0
            );
            gpgFile.IdentityName = alias;
            gpgFile.FileNamePublic = fileName;
            gpgFile.Description = window.Description;
            gpgFile.IsImported = true;

            service.Save(gpgFile);

            LoadGpgFileList();
            WrapperSelectedChangedEvent();

            GpgStatusMessage = $"成功导入: {alias}";
            GpgStatusColor = new SolidColorBrush(Colors.ForestGreen);
        }
        catch (Exception e)
        {
            MessageBox.Show($"导入失败: {e.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 备份选中 — 将勾选的密钥完整序列化到 JSON 文件（含私钥与口令）
    /// </summary>
    private void BackupSelectedGpgFiles(object obj)
    {
        if (GpgFileWrapperList == null) return;

        List<GpgFile> selected = GpgFileWrapperList
            .Where(x => x.IsSelected)
            .Select(x => x.GpgFile)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选要备份的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        WriteBackup(selected);
    }

    /// <summary>
    /// 全部备份 — 将当前列表所有密钥完整序列化到 JSON 文件
    /// </summary>
    private void BackupAllGpgFiles(object obj)
    {
        if (GpgFileWrapperList == null || GpgFileWrapperList.Count == 0)
        {
            MessageBox.Show("当前没有可备份的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        WriteBackup(GpgFileWrapperList.Select(x => x.GpgFile).ToList());
    }

    /// <summary>
    /// 写入备份文件
    /// </summary>
    private void WriteBackup(List<GpgFile> files)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Title = "备份 GPG 密钥",
            FileName = $"GpgBackup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            Filter = "GPG 备份文件 (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (saveFileDialog.ShowDialog() != true) return;
        string backupPath = saveFileDialog.FileName;

        try
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(files, options);
            File.WriteAllText(backupPath, json, Encoding.UTF8);

            GpgStatusMessage = $"已备份 {files.Count} 个密钥到: {backupPath}";
            GpgStatusColor = new SolidColorBrush(Colors.ForestGreen);
            MessageBox.Show(
                $"已备份 {files.Count} 个密钥到:\n{backupPath}\n\n注意: 备份文件包含私钥与口令，请妥善保管。",
                "备份成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show($"备份失败: {e.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 恢复备份 — 读取 JSON 备份文件并重新导入密钥（已存在的身份跳过）
    /// </summary>
    private void RestoreGpgBackup(object obj)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = "选择备份文件",
            Filter = "GPG 备份文件 (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() != true) return;

        try
        {
            string json = File.ReadAllText(openFileDialog.FileName);
            List<GpgFile>? backup = JsonSerializer.Deserialize<List<GpgFile>>(json);
            if (backup == null || backup.Count == 0)
            {
                MessageBox.Show("备份文件中没有可恢复的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"备份文件中包含 {backup.Count} 个密钥，是否恢复？\n已存在的密钥将被跳过。",
                "确认恢复",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            GpgFileService service = new GpgFileService(_myDbContext);
            HashSet<string> existingIdentities = _myDbContext.GpgFile
                .Select(f => f.Identity)
                .ToHashSet();

            int restored = 0;
            int skipped = 0;
            foreach (GpgFile item in backup)
            {
                if (existingIdentities.Contains(item.Identity))
                {
                    skipped++;
                    continue;
                }

                // 重新生成 ID，避免与库中已存在记录冲突
                item.Id = new SnowflakeId(1, 1).NextId();
                item.UpdateTime = DateTime.Now;
                service.Save(item);
                existingIdentities.Add(item.Identity);
                restored++;
            }

            LoadGpgFileList();
            WrapperSelectedChangedEvent();

            GpgStatusMessage = $"恢复完成: 新增 {restored} 个, 跳过 {skipped} 个";
            GpgStatusColor = new SolidColorBrush(Colors.ForestGreen);
            MessageBox.Show(
                $"恢复完成: 新增 {restored} 个密钥, 跳过 {skipped} 个已存在的密钥。",
                "恢复完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show($"恢复失败: {e.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 从 PGP 公钥 ASCII-armored 文本中提取身份（email）
    /// </summary>
    private static string ExtractIdentityFromPublicKey(string publicKey)
    {
        // PGP 公钥中包含类似 "uid [ultimate] boniu-w@live.cn" 的行
        foreach (string line in publicKey.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("uid "))
            {
                // 去掉 "uid " 前缀和可能的状态标记如 [ultimate]
                string uid = trimmed[4..].Trim();
                int bracketIdx = uid.IndexOf(']');
                if (bracketIdx >= 0)
                    uid = uid[(bracketIdx + 1)..].Trim();
                return uid;
            }
        }
        return "";
    }

    private string _selectedFilePath;

    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            _selectedFilePath = value;
            OnPropertyChanged();
        }
    }

    private string _gpgStatusMessage = "当前无新消息";

    public string GpgStatusMessage
    {
        get => _gpgStatusMessage;
        set
        {
            _gpgStatusMessage = value;
            OnPropertyChanged();
        }
    }

    private SolidColorBrush _gpgStatusColor = new SolidColorBrush(Colors.Red);

    public SolidColorBrush GpgStatusColor
    {
        get => _gpgStatusColor;
        set
        {
            _gpgStatusColor = value;
            OnPropertyChanged();
        }
    }

    private string _gpgFileSelectedOne;
    public string GpgFileSelectedOne
    {
        get => _gpgFileSelectedOne;
        set
        {
            _gpgFileSelectedOne = value;
            OnPropertyChanged();
        }
    }

    private string _emailText;
    public string EmailText
    {
        get => _emailText;
        set
        {
            _emailText = value;
            OnPropertyChanged();
        }
    }

    private string _passphraseText;
    public string PassphraseText
    {
        get => _passphraseText;
        set
        {
            _passphraseText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// GpgFileEntryWrapper.isSelected 事件
    /// </summary>
    public void WrapperSelectedChangedEvent()
    {
        if (GpgFileWrapperList != null)
        {
            foreach (GpgFileEntryWrapper wrapper in GpgFileWrapperList)
            {
                wrapper.SelectedChanged += Wrapper_SelectedChanged;
            }
        }


        // 如果 GpgFileWrapperList 会动态增加元素，需要在 Add 时订阅事件
        //GpgFileWrapperList.CollectionChanged += (s, e) =>
        //{
        //    if (e.NewItems != null)
        //        foreach (GpgFileEntryWrapper item in e.NewItems)
        //            item.SelectedChanged += Wrapper_SelectedChanged;
        //};
    }
    private void Wrapper_SelectedChanged(object? sender, EventArgs e)
    {
        if (GpgFileWrapperList != null)
        {
            GpgFileEntryWrapper? selected = GpgFileWrapperList.FirstOrDefault(x => x.IsSelected);
            GpgFileSelectedOne = selected?.GpgFile.FileNamePublic ?? string.Empty;
            UpdateIsGpgAllSelected();
        }
    }

    /// <summary>
    /// 去除文件名中的非法字符
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sanitized = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sanitized.Append(invalid.Contains(c) ? '_' : c);
        }
        return sanitized.ToString().Trim();
    }

    /// <summary>
    /// 处理重名：name.asc, name_2.asc, name_3.asc ...
    /// </summary>
    private static string GetUniqueEntryName(Dictionary<string, int> usedNames, string baseName, string ext)
    {
        if (usedNames.TryGetValue(baseName, out int count))
        {
            count++;
            usedNames[baseName] = count;
            return $"{baseName}_{count}{ext}";
        }
        else
        {
            usedNames[baseName] = 1;
            return $"{baseName}{ext}";
        }
    }
}