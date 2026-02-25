using Microsoft.Win32;
using PracticalTools.GPG;
using PracticalTools.vo;
using System.Windows;
using System.Windows.Controls;

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

    public async void DencryptFile(object sender, RoutedEventArgs e)
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
            MessageBox.Show("只能选一个文件进行解密",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Question);
            return;
        }

        try
        {
            // 只处理一个选中条目
            var gpgFileWrapper = entryWrappers[0];
            string outPath = dc.SelectedFilePath + ".txt";
            GpgFile file = gpgFileWrapper.GpgFile;
            string passphrase = file.Passphrase;
            string privateKey = file.SecretKey;
            // 异步调用解密方法
            await GpgHelper.DecryptFileByPublicString(dc.SelectedFilePath, outPath, privateKey, passphrase);
            // 成功提示
            MessageBox.Show($"解密成功！输出路径:\n{outPath}",
                            "成功",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
            ButtonTextBlockDe.Text = outPath;
        }
        catch (Exception ex)
        {
            // 失败提示
            MessageBox.Show($"解密失败：{ex.Message}",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
        }
    }
}