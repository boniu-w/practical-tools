using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
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

    public MainViewModel()
    {
        _myDbContext = new MyDbContext();
        FileList = new ObservableCollection<TheFile>(_myDbContext.DuplicateFiles.OrderByDescending(e => e.FileCreateTime).ToList());
        _blacklists = new ObservableCollection<Blacklist.Model.Blacklist>(_myDbContext.Blacklist.OrderByDescending(e => e.UpdateTime));
        // _myDbContext.DataChanged += OnDataChanged;
        // LoadDataAsync();
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
            OnPropertyChanged(nameof(Blacklists));
        }
    }

    // private async void OnDataChanged(object sender, DataChangedEventArgs e)
    // {
    //     if (e.EntityName == "Blacklist")
    //     {
    //         await LoadDataAsync();
    //     }
    // }

    // private async Task LoadDataAsync()
    // {
    //     var data = await _myDbContext.Blacklist.ToListAsync();
    //     // 通过 Dispatcher 确保 UI 线程更新
    //     Application.Current.Dispatcher.Invoke(() => { Blacklists = new ObservableCollection<Blacklist.Model.Blacklist>(data); });
    // }
}