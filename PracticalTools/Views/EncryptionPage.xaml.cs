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

    public async void EncryptFile(object sender, RoutedEventArgs e)
    {
        // 获取父级 ViewModel
        if (DataContext is not MainViewModel dc)
            return;

        // 获取选中的文件条目
        var entryWrappers = dc.GpgFileWrapperList?.Where(wrapper => wrapper.IsSelected).ToList();

        if (entryWrappers == null || entryWrappers.Count == 0)
        {
            MessageBox.Show("没有选中文件",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
            return;
        }

        if (entryWrappers.Count > 1)
        {
            MessageBox.Show("只能选一个文件进行加密",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Question);
            return;
        }

        try
        {
            // 只处理一个选中条目
            var gpgFileWrapper = entryWrappers[0];
            string outPath = dc.SelectedFilePath + ".gpg";
            // 异步调用加密方法
            await GpgHelper.EncryptFileByPublicString(dc.SelectedFilePath, outPath, gpgFileWrapper.GpgFile.PublicKey);

            // 成功提示
            MessageBox.Show($"加密成功！输出路径:\n{outPath}",
                            "成功",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
            OutputPathText.Text = $"输出: {outPath}";
        }
        catch (Exception ex)
        {
            // 失败提示
            MessageBox.Show($"加密失败：{ex.Message}",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
        }
    }

}