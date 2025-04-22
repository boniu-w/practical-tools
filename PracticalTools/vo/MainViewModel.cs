using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
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

    public ICommand RefreshCommand { get; }
    private ObservableCollection<DuplicateFilesEntry>? _fileList;

    public ObservableCollection<DuplicateFilesEntry>? FileList
    {
        get => _fileList;
        set
        {
            _fileList = value;
            OnPropertyChanged(nameof(FileList));
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

    private void RefreshBlackList()
    {
        LoadBlackLists();
    }

    private void LoadBlackLists()
    {
        try
        {
            FileList = new ObservableCollection<DuplicateFilesEntry>(_myDbContext.DuplicateFiles.OrderByDescending(e => e.FileCreateTime).ToList());
            Blacklists = new ObservableCollection<BlacklistEntry>(_myDbContext.Blacklist.OrderByDescending(e => e.UpdateTime));
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