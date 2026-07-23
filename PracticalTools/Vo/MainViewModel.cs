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
                    foreach (var item in GpgFileWrapperList)
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
        var count = GpgFileWrapperList.Count(x => x.IsSelected);
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

        var selected = GpgFileWrapperList.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选要删除的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定要删除选中的 {selected.Count} 个密钥吗？\n此操作不可撤销。",
            "确认批量删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var service = new GpgFileService(_myDbContext);
            foreach (var wrapper in selected)
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

        var selected = GpgFileWrapperList.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选要导出的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Title = "导出密钥到 Excel",
            FileName = $"GpgKeys_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
            DefaultExt = ".xlsx"
        };

        if (saveFileDialog.ShowDialog() != true) return;
        string filePath = saveFileDialog.FileName;

        try
        {
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("GPG密钥");

                // 表头
                string[] headers =
                {
                    "身份", "别名", "密钥长度", "有效期开始于", "有效期截至于",
                    "有效时长(秒)", "私钥文件名", "公钥文件名", "说明", "创建时间", "更新时间"
                };

                for (int col = 0; col < headers.Length; col++)
                {
                    var cell = worksheet.Cells[1, col + 1];
                    cell.Value = headers[col];
                    cell.Style.Font.Bold = true;
                }

                // 数据行
                for (int row = 0; row < selected.Count; row++)
                {
                    var gpg = selected[row].GpgFile;
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

                // 自动列宽
                worksheet.Cells.AutoFitColumns();

                // 保存 Excel
                package.SaveAs(new FileInfo(filePath));
            }

            // 导出公钥和私钥 .asc 文件到同目录
            string dir = Path.GetDirectoryName(filePath)!;
            int fileCount = 0;
            foreach (var wrapper in selected)
            {
                var gpg = wrapper.GpgFile;

                if (!string.IsNullOrEmpty(gpg.PublicKey))
                {
                    string pubPath = Path.Combine(dir, gpg.FileNamePublic);
                    File.WriteAllText(pubPath, gpg.PublicKey);
                    fileCount++;
                }

                if (!string.IsNullOrEmpty(gpg.SecretKey))
                {
                    string secPath = Path.Combine(dir, gpg.FileNamePrivate);
                    File.WriteAllText(secPath, gpg.SecretKey);
                    fileCount++;
                }
            }

            MessageBox.Show(
                $"已导出 {selected.Count} 个密钥对到:\n{dir}\n\nExcel 汇总: {Path.GetFileName(filePath)}\n密钥文件: {fileCount} 个 .asc",
                "导出成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show($"导出失败: {e.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPublic(object obj)
    {
        if (GpgFileWrapperList == null) return;

        var selected = GpgFileWrapperList.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选要导出的密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveFileDialog = new SaveFileDialog
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

            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // 1. 生成 Excel 并写入 ZIP
                using (var excelPackage = new ExcelPackage())
                {
                    var worksheet = excelPackage.Workbook.Worksheets.Add("GPG公钥");

                    string[] headers =
                    {
                        "身份", "别名", "密钥长度", "有效期开始于", "有效期截至于",
                        "有效时长(秒)", "公钥文件名", "说明", "创建时间", "更新时间"
                    };

                    for (int col = 0; col < headers.Length; col++)
                    {
                        var cell = worksheet.Cells[1, col + 1];
                        cell.Value = headers[col];
                        cell.Style.Font.Bold = true;
                    }

                    for (int row = 0; row < selected.Count; row++)
                    {
                        var gpg = selected[row].GpgFile;
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
                    var excelEntry = archive.CreateEntry("GPG公钥汇总.xlsx");
                    using (var entryStream = excelEntry.Open())
                    {
                        excelPackage.SaveAs(entryStream);
                    }
                }

                // 2. 将公钥 .asc 文件写入 ZIP
                int ascCount = 0;
                foreach (var wrapper in selected)
                {
                    var gpg = wrapper.GpgFile;
                    if (!string.IsNullOrEmpty(gpg.PublicKey))
                    {
                        var ascEntry = archive.CreateEntry(gpg.FileNamePublic);
                        using (var entryStream = ascEntry.Open())
                        using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
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
        var window = new ImportGpgWindow();
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
            var existing = _myDbContext.GpgFile.FirstOrDefault(f =>
                f.Identity == identity || f.FileNamePublic == fileName);
            if (existing != null)
            {
                MessageBox.Show($"公钥 \"{identity}\" 已存在 (ID: {existing.Id})，无法重复导入。",
                                "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var service = new GpgFileService(_myDbContext);
            var gpgFile = service.GenerateGpgFile(
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
    /// 从 PGP 公钥 ASCII-armored 文本中提取身份（email）
    /// </summary>
    private static string ExtractIdentityFromPublicKey(string publicKey)
    {
        // PGP 公钥中包含类似 "uid [ultimate] boniu-w@live.cn" 的行
        foreach (var line in publicKey.Split('\n'))
        {
            var trimmed = line.Trim();
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
            foreach (var wrapper in GpgFileWrapperList)
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
            var selected = GpgFileWrapperList.FirstOrDefault(x => x.IsSelected);
            GpgFileSelectedOne = selected?.GpgFile.FileNamePublic ?? string.Empty;
            UpdateIsGpgAllSelected();
        }
    }
}