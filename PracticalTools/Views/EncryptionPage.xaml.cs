using System.ComponentModel;
using System.IO;
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

    private void SelectDefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择加密后默认保存的文件夹" };
        if (dialog.ShowDialog() == true)
        {
            DefaultFolderTextBox.Text = dialog.FolderName;
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
        if (DataContext is not MainViewModel dc)
            return;

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

        string outName = Path.GetFileName(dc.SelectedFilePath) + ".gpg";
        string outPath;

        if (DontAskCheckBox.IsChecked == true)
        {
            string defaultFolder = DefaultFolderTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(defaultFolder) || !Directory.Exists(defaultFolder))
            {
                MessageBox.Show("请先选择默认保存文件夹。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            outPath = Path.Combine(defaultFolder, outName);
        }
        else
        {
            // 弹窗选输出目录
            var folderDialog = new OpenFolderDialog { Title = "选择加密后的保存目录" };
            if (folderDialog.ShowDialog() != true) return;
            outPath = Path.Combine(folderDialog.FolderName, outName);
        }

        try
        {
            if (File.Exists(outPath))
            {
                var overwriteResult = MessageBox.Show(
                    $"输出路径已存在同名文件:\n{outPath}\n\n是否覆盖?",
                    "文件已存在",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (overwriteResult != MessageBoxResult.Yes)
                    return;
            }

            var gpgFileWrapper = entryWrappers[0];

            // 加密中提示 + 改变鼠标形状
            dc.GpgStatusMessage = "加密中...";
            dc.GpgStatusColor = new SolidColorBrush(Colors.DodgerBlue);
            OutputPathText.Text = "加密中...";
            Mouse.OverrideCursor = Cursors.Wait;
            await GpgHelper.EncryptFileByPublicString(dc.SelectedFilePath, outPath, gpgFileWrapper.GpgFile.PublicKey);

            dc.GpgStatusMessage = $"加密成功！输出: {outPath}";
            dc.GpgStatusColor = new SolidColorBrush(Colors.ForestGreen);
            OutputPathText.Text = $"加密成功！输出: {outPath}";
        }
        catch (Exception ex)
        {
            dc.GpgStatusMessage = $"加密失败：{ex.Message}";
            dc.GpgStatusColor = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}