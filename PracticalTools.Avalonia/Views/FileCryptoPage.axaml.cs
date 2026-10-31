using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;

namespace PracticalTools.Desktop.Views;

public partial class FileCryptoPage : UserControl
{
    private KeyLibrary? _library;
    private Task? _reloadTask;
    public void Configure(KeyLibrary library) => _library = library;

    public Task ReloadKeysAsync() => _reloadTask is { IsCompleted: false } ? _reloadTask : _reloadTask = ReloadKeysCoreAsync();

    private async Task ReloadKeysCoreAsync()
    {
        if (_library == null) return;
        try
        {
            var items = await _library.ListAsync();
            LibraryKeys.ItemsSource = Decrypt ? items.Where(k => k.HasSecret).ToList() : items;
        }
        catch (Exception ex) { UiStatus.Set(Status, ex.Message, UiStatusKind.Error); }
    }

    private async void RefreshLibrary(object? sender, RoutedEventArgs e) => await ReloadKeysAsync();
    private void UseManualKey(object? sender, RoutedEventArgs e) => LibraryKeys.SelectedItem = null;
    private void LibrarySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (KeyText == null || Passphrase == null) return;
        var selected = LibraryKeys.SelectedItem is KeySummary;
        KeyText.IsEnabled = Passphrase.IsEnabled = !selected;
        if (selected) { KeyText.Text = Passphrase.Text = ""; }
    }
    public static readonly StyledProperty<bool> DecryptProperty =
        AvaloniaProperty.Register<FileCryptoPage, bool>(nameof(Decrypt));

    public bool Decrypt { get => GetValue(DecryptProperty); set => SetValue(DecryptProperty, value); }

    public FileCryptoPage()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            Heading.Text = Decrypt ? "使用私钥解密文件" : "使用公钥加密文件";
            KeyLabel.Text = Decrypt ? "ASCII 私钥" : "ASCII 公钥";
            Passphrase.IsVisible = Decrypt;
            RunButton.Content = Decrypt ? "开始解密" : "开始加密";
            if (Decrypt) KeyText.PasswordChar = '●';
            await ReloadKeysAsync();
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
        catch (Exception ex) { UiStatus.Set(Status, ex.Message, UiStatusKind.Error); }
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
            LibraryKeys.SelectedItem = null;
            KeyText.Text = await reader.ReadToEndAsync();
        }
        catch (Exception ex) { UiStatus.Set(Status, $"导入失败：{ex.Message}", UiStatusKind.Error); }
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
        catch (Exception ex) { UiStatus.Set(Status, ex.Message, UiStatusKind.Error); }
    }

    private async void Run(object? sender, RoutedEventArgs e)
    {
        try
        {
            var input = InputPath.Text?.Trim();
            var output = OutputPath.Text?.Trim();
            Form.IsEnabled = false;
            Progress.IsVisible = true;
            var key = KeyText.Text;
            var passphrase = Passphrase.Text ?? "";
            if (_library != null && LibraryKeys.SelectedItem is KeySummary selected)
            {
                if (Decrypt)
                {
                    var material = await _library.GetSecretAsync(selected.Id);
                    key = material.Key;
                    passphrase = material.Passphrase;
                }
                else
                {
                    if (selected.EndsOn.HasValue && selected.EndsOn.Value.ToUniversalTime() <= DateTime.UtcNow)
                        throw new InvalidOperationException("该密钥已过期，请选择有效密钥加密。");
                    key = await _library.GetPublicKeyAsync(selected.Id);
                }
            }
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("请选择输入、输出文件并提供密钥。");
            Form.IsEnabled = false;
            Progress.IsVisible = true;
            UiStatus.Set(Status, Decrypt ? "正在解密…" : "正在加密…", UiStatusKind.Busy);
            if (Decrypt)
                await GpgFileOperations.DecryptAsync(input, output, key, passphrase);
            else
                await GpgFileOperations.EncryptAsync(input, output, key);
            UiStatus.Set(Status, $"处理成功：{output}", UiStatusKind.Success);
        }
        catch (Exception ex) { UiStatus.Set(Status, $"处理失败：{ex.Message}", UiStatusKind.Error); }
        finally
        {
            Form.IsEnabled = true;
            Progress.IsVisible = false;
        }
    }
}
