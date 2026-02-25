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
            }
        }
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
            GpgFileWrapperList = new ObservableCollection<GpgFileEntryWrapper>(_myDbContext.GpgFile.Select(e => new GpgFileEntryWrapper(e)));
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message);
            throw;
        }
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
        }
    }
}