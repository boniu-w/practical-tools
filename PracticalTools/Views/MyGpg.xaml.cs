using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticalTools.GPG;

namespace PracticalTools.Views;

public partial class MyGpg : UserControl
{
    public MyGpg()
    {
        InitializeComponent();
    }

    private async void OnEncryptClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string publicKeyFile = "H:\\wg\\gpg-key\\pub-9y.gpg";
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = Path.GetFileNameWithoutExtension(openFileDialog.FileName) + ".gpg",
                    Filter = "GPG Files (*.gpg)|*.gpg|All Files (*.*)|*.*"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    EncryptButton.IsEnabled = false;
                    StatusText.Text = "正在加密...";

                    // 显示“处理中”状态，比如禁用按钮
                    EncryptButton.IsEnabled = false;

                    await Task.Run(() => { GpgHelper.EncryptFile(openFileDialog.FileName, saveFileDialog.FileName, publicKeyFile); });

                    StatusText.Text = "加密完成";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加密失败: {ex.Message}";
        }
        finally
        {
            EncryptButton.IsEnabled = true;
        }
    }

    private async void OnDecryptClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string privateKeyFile = "H:\\wg\\gpg-key\\pri-9y.gpg";
            string passphrase = "Soccer0boy$";
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = Path.GetFileNameWithoutExtension(openFileDialog.FileName),
                    Filter = "All Files (*.*)|*.*"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    DecryptButton.IsEnabled = false;
                    StatusText.Text = "正在解密...";

                    // 显示“处理中”状态，比如禁用按钮
                    DecryptButton.IsEnabled = false;

                    await Task.Run(() => { GpgHelper.DecryptFile(openFileDialog.FileName, saveFileDialog.FileName, privateKeyFile, passphrase); });

                    StatusText.Text = "解密完成";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"解密失败: {ex.Message}";
        }
        finally
        {
            DecryptButton.IsEnabled = true;
        }
    }
}