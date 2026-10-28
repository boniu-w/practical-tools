using System.IO;
using Microsoft.Win32;
using PracticalTools.GPG;
using PracticalTools.util;
using PracticalTools.vo;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PracticalTools.Views;

public partial class DencryptionFolderPage : UserControl
{
    private string? _selectedGpgPath;

    public DencryptionFolderPage()
    {
        InitializeComponent();
    }

    private void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要解密的文件",
            Filter = "GPG 文件 (*.gpg)|*.gpg|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedGpgPath = dialog.FileName;
            GpgPathTextBox.Text = dialog.FileName;
        }
    }

    public async void DencryptFolder(object sender, RoutedEventArgs e)
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
            MessageBox.Show("只能选一个密钥进行解密",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Question);
            return;
        }

        if (string.IsNullOrEmpty(_selectedGpgPath) || !File.Exists(_selectedGpgPath))
        {
            MessageBox.Show("请先选择一个 .gpg 文件",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
            return;
        }

        // 弹窗选解压目录
        var folderDialog = new OpenFolderDialog { Title = "选择解密后解压到的目录" };
        if (folderDialog.ShowDialog() != true) return;

        string gpgFileName = Path.GetFileName(_selectedGpgPath);
        string baseName = gpgFileName.EndsWith(".zip.gpg", StringComparison.OrdinalIgnoreCase)
            ? gpgFileName[..^8]
            : gpgFileName.EndsWith(".gpg", StringComparison.OrdinalIgnoreCase)
                ? gpgFileName[..^4]
                : Path.GetFileNameWithoutExtension(gpgFileName);
        string extractDir = Path.Combine(folderDialog.FolderName, baseName);

        string tempZip = Path.Combine(Path.GetTempPath(), $"dec_{Guid.NewGuid():N}.zip");

        try
        {
            // 解密中提示 + 改变鼠标形状
            dc.GpgStatusMessage = "解密中...";
            dc.GpgStatusColor = new SolidColorBrush(Colors.DodgerBlue);
            OutputPathText.Text = "解密中...";
            Mouse.OverrideCursor = Cursors.Wait;

            // 先解密出 zip，再解压到目标目录
            var gpgFileWrapper = entryWrappers[0];
            GpgFile file = gpgFileWrapper.GpgFile;
            await GpgHelper.DecryptFileByPublicString(_selectedGpgPath, tempZip, file.SecretKey, file.Passphrase);

            FileUtil.ExtractZip(tempZip, extractDir);

            dc.GpgStatusMessage = $"解密成功！解压到: {extractDir}";
            dc.GpgStatusColor = new SolidColorBrush(Colors.ForestGreen);
            OutputPathText.Text = $"解密成功！解压到: {extractDir}";
        }
        catch (Exception ex)
        {
            dc.GpgStatusMessage = $"解密失败：{ex.Message}";
            dc.GpgStatusColor = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }
}