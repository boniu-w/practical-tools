using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;

namespace PracticalTools.Desktop.Views;

public partial class GenerateKeyPage : UserControl
{
    private string? _secretKey;
    public GenerateKeyPage() => InitializeComponent();

    private async void Generate(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_secretKey != null)
                throw new InvalidOperationException("当前会话已生成密钥，请先导出保存；需要新密钥时重新打开应用。");
            var identity = Identity.Text?.Trim();
            var passphrase = Passphrase.Text ?? "";
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(passphrase))
                throw new ArgumentException("请输入身份和私钥保护口令。");
            if (passphrase != ConfirmPassphrase.Text) throw new ArgumentException("两次输入的口令不一致。");
            Form.IsEnabled = false;
            Progress.IsVisible = true;
            Status.Text = "正在生成密钥…";
            var keys = await Task.Run(() =>
            {
                var pair = GpgHelper.GeneratePgpKeyPair(identity, passphrase, 3072);
                return (Public: GpgHelper.ExportPublicKey(pair.pubRing),
                    Secret: GpgHelper.ExportPrivateKey(pair.secRing),
                    Fingerprint: GpgHelper.GetFingerprint(pair.pubRing));
            });
            PublicKey.Text = keys.Public;
            _secretKey = keys.Secret;
            Fingerprint.Text = $"公钥指纹：{keys.Fingerprint}";
            ExportPublic.IsEnabled = ExportSecret.IsEnabled = true;
            Passphrase.Text = ConfirmPassphrase.Text = "";
            Status.Text = "生成成功，请分别导出公钥和私钥。";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { Form.IsEnabled = true; Progress.IsVisible = false; }
    }

    private async void SavePublic(object? sender, RoutedEventArgs e) => await SaveAsync(PublicKey.Text, "public.asc");
    private async void SaveSecret(object? sender, RoutedEventArgs e) => await SaveAsync(_secretKey, "secret.asc");

    private async Task SaveAsync(string? content, string name)
    {
        if (string.IsNullOrEmpty(content)) return;
        try
        {
            using var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "导出密钥（请选择新文件名）", SuggestedFileName = name
            });
            if (file == null) return;
            var path = file.TryGetLocalPath() ?? throw new IOException("请选择本地保存位置。");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var stream = new FileStream(path, options);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
            Status.Text = $"已导出：{path}";
        }
        catch (Exception ex) { Status.Text = $"导出失败：{ex.Message}"; }
    }
}
