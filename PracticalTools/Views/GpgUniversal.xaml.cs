using System.Windows;
using System.Windows.Controls;

namespace PracticalTools.Views;

public partial class GpgUniversal : UserControl
{
    public GpgUniversal()
    {
        InitializeComponent();
    }

    private void EncryptMode_Checked(object sender, RoutedEventArgs e)
    {
        // 首次 IsChecked="True" 会在 InitializeComponent 解析期间触发，此时 x:Name 字段尚未赋值，需判空
        if (FileEncryptPage == null || FolderEncryptPage == null)
            return;

        bool isFolder = EncryptFolderRadio.IsChecked == true;
        FileEncryptPage.Visibility = isFolder ? Visibility.Collapsed : Visibility.Visible;
        FolderEncryptPage.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DecryptMode_Checked(object sender, RoutedEventArgs e)
    {
        if (FileDecryptPage == null || FolderDecryptPage == null)
            return;

        bool isFolder = DecryptFolderRadio.IsChecked == true;
        FileDecryptPage.Visibility = isFolder ? Visibility.Collapsed : Visibility.Visible;
        FolderDecryptPage.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
    }
}