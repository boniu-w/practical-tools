using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.DuplicateFiles.Wrapper;
using PracticalTools.Sqlite;
using PracticalTools.util;
using PracticalTools.Views;

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

    private readonly MyDbContext _myDbContext;

    public MainViewModel(MyDbContext myDbContext)
    {
        _myDbContext = myDbContext;
        RefreshCommand = new RelayCommand<Object>(RefreshBlackList);
        RefreshBlackList<Object>(null);
    }

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
}