using Avalonia.Controls;
using Avalonia.Interactivity;
using PracticalTools.GPG;

namespace PracticalTools.Desktop.Views;

public partial class KeyImportWindow : Window
{
    private readonly KeyLibrary? _library;
    private string? _armoredKey;
    public KeyImportWindow() { InitializeComponent(); Form.IsEnabled = false; Status.Text = "请从密钥库打开此窗口。"; }
    public KeyImportWindow(KeyLibrary library) : this() { _library = library; Form.IsEnabled = true; Status.Text = null; }
    private async void SelectFile(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "选择密钥文件", AllowMultiple = false });
            if (files.Count == 0) return;
            using var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            _armoredKey = await reader.ReadToEndAsync();
            FileName.Text = file.Name;
        }
        catch (Exception ex) { Status.Text = $"读取失败：{ex.Message}"; }
    }
    private async void Import(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_library == null) throw new InvalidOperationException("密钥库尚未配置。");
            if (string.IsNullOrWhiteSpace(_armoredKey)) throw new ArgumentException("请先选择密钥文件。");
            Form.IsEnabled = false;
            await _library.ImportAsync(_armoredKey, Passphrase.Text ?? "", KeyName.Text ?? "", Description.Text ?? "");
            Passphrase.Text = "";
            Close(true);
        }
        catch (Exception ex) { Status.Text = $"导入失败：{ex.Message}"; }
        finally { Form.IsEnabled = true; }
    }
}
