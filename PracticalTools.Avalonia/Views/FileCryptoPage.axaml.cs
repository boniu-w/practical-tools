using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;

namespace PracticalTools.Desktop.Views;

public partial class FileCryptoPage : UserControl
{
    public static readonly StyledProperty<bool> DecryptProperty =
        AvaloniaProperty.Register<FileCryptoPage, bool>(nameof(Decrypt));

    public bool Decrypt { get => GetValue(DecryptProperty); set => SetValue(DecryptProperty, value); }

    public FileCryptoPage()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            Heading.Text = Decrypt ? "使用私钥解密文件" : "使用公钥加密文件";
            KeyLabel.Text = Decrypt ? "ASCII 私钥" : "ASCII 公钥";
            Passphrase.IsVisible = Decrypt;
            RunButton.Content = Decrypt ? "开始解密" : "开始加密";
        };
    }

    private IStorageProvider Storage => TopLevel.GetTopLevel(this)!.StorageProvider;

    private async void SelectInput(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await Storage.OpenFilePickerAsync(new() { Title = "选择输入文件", AllowMultiple = false });
            if (files.Count > 0)
            {
                using var file = files[0];
                InputPath.Text = file.TryGetLocalPath() ?? throw new IOException("请选择本地文件。");
            }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void ImportKey(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await Storage.OpenFilePickerAsync(new() { Title = "选择 ASCII 密钥文件", AllowMultiple = false });
            if (files.Count == 0) return;
            using var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            KeyText.Text = await reader.ReadToEndAsync();
        }
        catch (Exception ex) { Status.Text = $"导入失败：{ex.Message}"; }
    }

    private async void SelectOutput(object? sender, RoutedEventArgs e)
    {
        try
        {
            var name = Path.GetFileName(InputPath.Text ?? "");
            if (string.IsNullOrEmpty(name)) name = "output";
            name = Decrypt
                ? (name.EndsWith(".gpg", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name + ".decrypted")
                : name + ".gpg";
            using var file = await Storage.SaveFilePickerAsync(new() { Title = "选择新的输出路径", SuggestedFileName = name });
            if (file != null)
                OutputPath.Text = file.TryGetLocalPath() ?? throw new IOException("请选择本地保存位置。");
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void Run(object? sender, RoutedEventArgs e)
    {
        try
        {
            var input = InputPath.Text?.Trim();
            var output = OutputPath.Text?.Trim();
            var key = KeyText.Text;
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("请选择输入、输出文件并提供密钥。");
            Form.IsEnabled = false;
            Progress.IsVisible = true;
            Status.Text = Decrypt ? "正在解密…" : "正在加密…";
            if (Decrypt)
                await GpgFileOperations.DecryptAsync(input, output, key, Passphrase.Text ?? "");
            else
                await GpgFileOperations.EncryptAsync(input, output, key);
            Status.Text = $"处理成功：{output}";
        }
        catch (Exception ex) { Status.Text = $"处理失败：{ex.Message}"; }
        finally
        {
            Form.IsEnabled = true;
            Progress.IsVisible = false;
        }
    }
}
