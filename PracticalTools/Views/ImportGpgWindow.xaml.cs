using System.Windows;
using Microsoft.Win32;

namespace PracticalTools.Views;

public partial class ImportGpgWindow : Window
{
    public ImportGpgWindow()
    {
        InitializeComponent();
    }

    public string PublicKeyPath { get; private set; } = "";
    public string Alias { get; private set; } = "";
    public string Description { get; private set; } = "";

    private void SelectPublicKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择公钥文件",
            Filter = "ASC Files (*.asc)|*.asc|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            TxtPublicKey.Text = dialog.FileName;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtPublicKey.Text))
        {
            MessageBox.Show("请选择公钥文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PublicKeyPath = TxtPublicKey.Text;
        Alias = TxtAlias.Text.Trim();
        Description = TxtDescription.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
