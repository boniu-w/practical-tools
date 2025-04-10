using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.Sqlite;
using PracticalTools.util;

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

    // public MainViewModel()
    // {
    //     _myDbContext = new MyDbContext();
    //     FileList = new ObservableCollection<TheFile>(_myDbContext.DuplicateFiles.OrderByDescending(e => e.FileCreateTime).ToList());
    //     _blacklists = new ObservableCollection<Blacklist.Model.Blacklist>(_myDbContext.Blacklist.OrderByDescending(e => e.UpdateTime));
    // }
    public MainViewModel() : this(new MyDbContext())
    {
    }

    public MainViewModel(MyDbContext myDbContext)
    {
        _myDbContext = myDbContext;
        RefreshCommand = new RelayCommand(RefreshBlackList);
        RefreshBlackList();
    }

    public ICommand RefreshCommand { get; }
    private ObservableCollection<TheFile>? _fileList;

    public ObservableCollection<TheFile>? FileList
    {
        get => _fileList;
        set
        {
            _fileList = value;
            OnPropertyChanged(nameof(FileList));
        }
    }

    private ObservableCollection<Blacklist.Model.Blacklist> _blacklists;

    public ObservableCollection<Blacklist.Model.Blacklist> Blacklists
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
        Blacklists = new ObservableCollection<Blacklist.Model.Blacklist>(_myDbContext.Blacklist.OrderByDescending(e => e.UpdateTime));
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