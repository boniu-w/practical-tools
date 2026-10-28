using System.Windows;
using System.Windows.Controls;

namespace PracticalTools.Views;

public partial class GpgUniversal : UserControl
{
    private bool _previewExpanded;
    private GridLength _keyListHeight;

    public GpgUniversal()
    {
        InitializeComponent();
        FileEncryptPage.EncryptionCompleted += PreviewPage.SetEncryptedFile;
        PreviewPage.ExpandedChanged += expanded =>
        {
            if (_previewExpanded == expanded) return;
            _previewExpanded = expanded;
            if (expanded) _keyListHeight = KeyListRow.Height;
            KeyListPanel.Visibility = PreviewSplitter.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            KeyListRow.Height = expanded ? new GridLength(0) : _keyListHeight;
            PreviewSplitterRow.Height = new GridLength(expanded ? 0 : 6);
        };
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
