using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PracticalTools.GPG;

public class GpgFileEntryWrapper : INotifyPropertyChanged
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

    public GpgFileEntryWrapper(GpgFile gpgFile)
    {
        GpgFile = gpgFile;
    }

    public GpgFile GpgFile { get; set; }
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
                // 当勾选状态变化时，通知 MainViewModel 更新 GpgFileSelectedOne
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectedChanged;
}