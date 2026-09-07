using System.IO;
using Microsoft.Win32;
using PracticalTools.GPG;
using PracticalTools.vo;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PracticalTools.Views;

/// <summary>
/// DencryptionPage.xaml 的交互逻辑
/// </summary>
public partial class DencryptionPage : UserControl
{
    public DencryptionPage()
    {
        InitializeComponent();
    }

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
        var dialog = new OpenFolderDialog { Title = "选择解密后默认保存的文件夹" };
        if (dialog.ShowDialog() == true)
        {
            DefaultFolderTextBox.Text = dialog.FolderName;
        }
    }

    public async void DencryptFile(object sender, RoutedEventArgs e)
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
            MessageBox.Show("只能选一个文件进行解密",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Question);
            return;
        }

        string inputFileName = Path.GetFileName(dc.SelectedFilePath);
        string outName = inputFileName.EndsWith(".gpg", StringComparison.OrdinalIgnoreCase)
            ? inputFileName[..^4]
            : inputFileName + ".decrypted";

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
            var folderDialog = new OpenFolderDialog { Title = "选择解密后的保存目录" };
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
            GpgFile file = gpgFileWrapper.GpgFile;
            string passphrase = file.Passphrase;
            string privateKey = file.SecretKey;
            await GpgHelper.DecryptFileByPublicString(dc.SelectedFilePath, outPath, privateKey, passphrase);

            dc.GpgStatusMessage = $"解密成功！输出: {outPath}";
            dc.GpgStatusColor = new SolidColorBrush(Colors.ForestGreen);
            OutputPathText.Text = $"解密成功！输出: {outPath}";
        }
        catch (Exception ex)
        {
            dc.GpgStatusMessage = $"解密失败：{ex.Message}";
            dc.GpgStatusColor = new SolidColorBrush(Colors.Red);
        }
    }
}
