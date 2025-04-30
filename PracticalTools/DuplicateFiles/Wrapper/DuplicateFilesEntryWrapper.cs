using System.ComponentModel;
using System.Runtime.CompilerServices;
using PracticalTools.DuplicateFiles.Model;

namespace PracticalTools.DuplicateFiles.Wrapper;

public class DuplicateFilesEntryWrapper : INotifyPropertyChanged
{
    public DuplicateFilesEntryWrapper(DuplicateFilesEntry entry)
    {
        Entry = entry;
        
    }
    
    public DuplicateFilesEntry Entry { get; set; }

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

    public override string ToString()
    {
        return $"Id: {Entry.Id}" +
               $"FileName: {Entry.FileName}" +
               $"FilePath: {Entry.FilePath}" +
               $"FileCreateTime: {Entry.FileCreateTime}" +
               $"FileSize: {Entry.FileSize}" +
               $"FileLastedModifiedTime: {Entry.FileLastedModifiedTime}" +
               $"ScanDirectory: {Entry.ScanDirectory}" +
               $"ScanTime: {Entry.ScanTime}" +
               $"HashValue: {Entry.Md5Value}";
    }
    
    /**************************************************************************************************************/

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
}

