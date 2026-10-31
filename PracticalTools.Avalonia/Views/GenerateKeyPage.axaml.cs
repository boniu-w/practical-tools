using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;

namespace PracticalTools.Desktop.Views;

public partial class GenerateKeyPage : UserControl
{
    private KeyLibrary? _library;
    private long? _generatedId;
    public void Configure(KeyLibrary library) => _library = library;
    public GenerateKeyPage() => InitializeComponent();

    private async void Generate(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_library == null) throw new InvalidOperationException("密钥库尚未初始化。");
            var identity = Identity.Text?.Trim();
            var passphrase = Passphrase.Text ?? "";
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(passphrase))
                throw new ArgumentException("请输入身份和私钥保护口令。");
            if (passphrase != ConfirmPassphrase.Text) throw new ArgumentException("两次输入的口令不一致。");
            Form.IsEnabled = false;
            Progress.IsVisible = true;
            UiStatus.Set(Status, "正在生成密钥…", UiStatusKind.Busy);
            var keys = await _library.GenerateAsync(identity, passphrase, (int)(KeyBits.Value ?? 3072), (int)(ValidDays.Value ?? 365));
            _generatedId = keys.Id;
            PublicKey.Text = await _library.GetPublicKeyAsync(keys.Id);
            Fingerprint.Text = $"公钥指纹：{keys.Fingerprint}";
            ExportPublic.IsEnabled = ExportSecret.IsEnabled = true;
            Passphrase.Text = ConfirmPassphrase.Text = "";
            UiStatus.Set(Status, "生成成功并已保存到密钥库，重启后可继续使用。", UiStatusKind.Success);
        }
        catch (Exception ex) { UiStatus.Set(Status, ex.Message, UiStatusKind.Error); }
        finally { Form.IsEnabled = true; Progress.IsVisible = false; }
    }

    private async void SavePublic(object? sender, RoutedEventArgs e) => await SaveAsync(false, "public.asc");
    private async void SaveSecret(object? sender, RoutedEventArgs e) => await SaveAsync(true, "secret.asc");

    private async Task SaveAsync(bool secret, string name)
    {
        if (_library == null || !_generatedId.HasValue) return;
        try
        {
            using var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "导出密钥（请选择新文件名）", SuggestedFileName = name
            });
            if (file == null) return;
            var path = file.TryGetLocalPath() ?? throw new IOException("请选择本地保存位置。");
            await _library.ExportAsync(_generatedId.Value, path, secret);
            UiStatus.Set(Status, $"已导出：{path}", UiStatusKind.Success);
        }
        catch (Exception ex) { UiStatus.Set(Status, $"导出失败：{ex.Message}", UiStatusKind.Error); }
    }
}
