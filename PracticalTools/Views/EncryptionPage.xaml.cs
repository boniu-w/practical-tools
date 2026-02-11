using System.ComponentModel;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PracticalTools.GPG;
using PracticalTools.util;
using PracticalTools.vo;

namespace PracticalTools.Views;

public partial class EncryptionPage : UserControl, INotifyPropertyChanged
{
    public EncryptionPage()
    {
        InitializeComponent();

       // DismissCommand = new RelayCommand<Object>(EncryptFile);
    }

    // public ICommand DismissCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public bool ShowDismissButton { get; set; } = true;



    private void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        var dc = DataContext as MainViewModel;
        var dialog = new OpenFileDialog
        {
            Filter = "所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            dc.SelectedFilePath = dialog.FileName;
        }
    }


    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void EncryptFile(object sender, RoutedEventArgs e)
    {
        var dc = DataContext as MainViewModel;
        List<GpgFileEntryWrapper>? entryWrappers = dc?.GpgFileWrapperList?.Where(wrapper => wrapper.IsSelected).ToList();
        
        if (entryWrappers?.Count > 1)
        {
            MessageBox.Show("只能选一个",
                    "提示",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
        }
        else if (entryWrappers?.Count == 1)
        {
            GpgFileEntryWrapper gpgFileWrapper = entryWrappers[0];
            
            GpgHelper.EncryptFile(dc.SelectedFilePath, dc.SelectedFilePath, gpgFileWrapper.GpgFile.PublicKey);
        }
        else
        {
            MessageBox.Show("有问题",
                    "提示",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
        }
    }
}