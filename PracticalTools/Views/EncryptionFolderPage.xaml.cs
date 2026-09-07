using System.IO;
using Microsoft.Win32;
using PracticalTools.GPG;
using PracticalTools.util;
using PracticalTools.vo;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PracticalTools.Views;

public partial class EncryptionFolderPage : UserControl
{
    private string? _selectedFolderPath;

    public EncryptionFolderPage()
    {
        InitializeComponent();
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择要加密的文件夹" };
        if (dialog.ShowDialog() == true)
        {
            _selectedFolderPath = dialog.FolderName;
            FolderPathTextBox.Text = dialog.FolderName;
        }
    }

    public async void EncryptFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel dc)
            return;

        var entryWrappers = dc.GpgFileWrapperList?.Where(wrapper => wrapper.IsSelected).ToList();

        if (entryWrappers == null || entryWrappers.Count == 0)
        {
            MessageBox.Show("没有选中密钥",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
            return;
        }

        if (entryWrappers.Count > 1)
        {
            MessageBox.Show("只能选一个密钥进行加密",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Question);
            return;
        }

        if (string.IsNullOrEmpty(_selectedFolderPath) || !Directory.Exists(_selectedFolderPath))
        {
            MessageBox.Show("请先选择一个文件夹",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
            return;
        }

        // 弹窗选输出目录
        var folderDialog = new OpenFolderDialog { Title = "选择加密后的保存目录" };
        if (folderDialog.ShowDialog() != true) return;

        string folderName = new DirectoryInfo(_selectedFolderPath).Name;
        string outName = folderName + ".zip.gpg";
        string outPath = Path.Combine(folderDialog.FolderName, outName);

        string tempZip = Path.Combine(Path.GetTempPath(), $"enc_{Guid.NewGuid():N}.zip");

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

            // 先打包成 zip，再用公钥加密
            FileUtil.ZipDirectory(_selectedFolderPath, tempZip);
            var gpgFileWrapper = entryWrappers[0];
            await GpgHelper.EncryptFileByPublicString(tempZip, outPath, gpgFileWrapper.GpgFile.PublicKey);

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
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }
}