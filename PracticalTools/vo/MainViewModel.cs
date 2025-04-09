using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public MainViewModel()
    {
        MyDbContext myDbContext = new MyDbContext();
        FileList = new ObservableCollection<TheFile>(myDbContext.DuplicateFiles);
        _blacklists = new ObservableCollection<Blacklist.Model.Blacklist>(myDbContext.Blacklist);
    }

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
            OnPropertyChanged();
        }
    }
}