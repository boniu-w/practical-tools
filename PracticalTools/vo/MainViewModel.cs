using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.DuplicateFiles.Wrapper;
using PracticalTools.Sqlite;

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
        RefreshCommand = new RelayCommand(RefreshBlackList);
        RefreshBlackList();
    }

    public List<DuplicateFilesEntry> DuplicateFilesEntries { get; set; }

    public ICommand RefreshCommand { get; }

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
                    foreach (var item in _fileList)
                        item.PropertyChanged -= FileItem_PropertyChanged;
                }

                _fileList = value;

                // 绑定新的
                if (_fileList != null)
                {
                    foreach (var item in _fileList)
                        item.PropertyChanged += FileItem_PropertyChanged;
                }

                OnPropertyChanged(nameof(FileList));
                UpdateIsAllSelected(); // 顺便刷新一下全选状态
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

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    private bool? _isAllSelected = false;

    public bool? IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (_isAllSelected != value)
            {
                _isAllSelected = value;
                if (FileList != null && value.HasValue)
                {
                    foreach (var item in FileList)
                    {
                        item.IsSelected = value.Value;
                    }
                }

                OnPropertyChanged(nameof(IsAllSelected));
            }
        }
    }

    /**************************************************************************************************************/
    
    private void FileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateFilesEntryWrapper.IsSelected))
        {
            UpdateIsAllSelected();
        }
    }

    private void UpdateIsAllSelected()
    {
        if (FileList == null || FileList.Count == 0)
        {
            IsAllSelected = false;
            return;
        }

        var selectedCount = FileList.Count(x => x.IsSelected);
        if (selectedCount == 0)
            IsAllSelected = false;
        else if (selectedCount == FileList.Count)
            IsAllSelected = true;
        else
            IsAllSelected = null; // 部分选中
    }


    private void RefreshBlackList()
    {
        LoadBlackLists();
    }
    
    private void LoadBlackLists()
    {
        try
        {
            DuplicateFilesEntries = _myDbContext.DuplicateFiles.OrderByDescending(e => e.FileCreateTime).ToList();

            // 直接赋值，不用管 PropertyChanged
            FileList = new ObservableCollection<DuplicateFilesEntryWrapper>(
                DuplicateFilesEntries.Select(e => new DuplicateFilesEntryWrapper(e))
            );

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
    
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute();
        }
    }
}