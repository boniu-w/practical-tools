using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;
using PracticalTools.GPG.Folders;

namespace PracticalTools.Desktop.Views;

public partial class FolderCryptoPage : UserControl
{
    public static readonly StyledProperty<bool> DecryptProperty = AvaloniaProperty.Register<FolderCryptoPage, bool>(nameof(Decrypt));
    public bool Decrypt { get => GetValue(DecryptProperty); set => SetValue(DecryptProperty, value); }
    private KeyLibrary? _library;
    private IFolderCryptoService _service = new FolderCryptoService();
    private Task? _reload;
    private CancellationTokenSource? _cancellation;

    public FolderCryptoPage()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            Heading.Text = Decrypt ? "解密并恢复文件夹" : "打包并加密文件夹";
            RunButton.Content = Decrypt ? "解密并恢复" : "打包并加密";
            InputPath.Watermark = Decrypt ? "输入 .zip.gpg 归档" : "输入目录";
            OutputPath.Watermark = Decrypt ? "恢复到新的完整目录路径" : "输出文件路径，如 folder.zip.gpg";
            await ReloadKeysAsync();
        };
        DetachedFromVisualTree += (_, _) => _cancellation?.Cancel();
    }

    public void Configure(KeyLibrary library, IFolderCryptoService? service = null)
    { _library = library; if (service != null) _service = service; }
    public Task ReloadKeysAsync() => _reload is { IsCompleted: false } ? _reload : _reload = ReloadCoreAsync();
    private async Task ReloadCoreAsync()
    {
        if (_library == null) return;
        try
        {
            var keys = await _library.ListAsync();
            LibraryKeys.ItemsSource = Decrypt ? keys.Where(k => k.HasSecret).ToList() : keys;
        }
        catch (Exception ex) { Status.Text = $"加载密钥失败：{ex.Message}"; }
    }
    private async void RefreshKeys(object? sender, RoutedEventArgs e) => await ReloadKeysAsync();

    private async void PickInput(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)!.StorageProvider;
            if (Decrypt)
            {
                var files = await storage.OpenFilePickerAsync(new() { Title = "选择文件夹加密归档", AllowMultiple = false });
                if (files.Count == 0) return;
                using var file = files[0]; InputPath.Text = file.TryGetLocalPath() ?? throw new IOException("请选择本地文件。");
            }
            else
            {
                var folders = await storage.OpenFolderPickerAsync(new() { Title = "选择输入文件夹", AllowMultiple = false });
                if (folders.Count == 0) return;
                using var folder = folders[0]; InputPath.Text = folder.TryGetLocalPath() ?? throw new IOException("请选择本地目录。");
            }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void PickOutput(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)!.StorageProvider;
            if (Decrypt)
            {
                var folders = await storage.OpenFolderPickerAsync(new() { Title = "选择恢复目录的父目录", AllowMultiple = false });
                if (folders.Count == 0) return;
                using var folder = folders[0];
                var parent = folder.TryGetLocalPath() ?? throw new IOException("请选择本地目录。");
                var name = Path.GetFileName(InputPath.Text ?? "folder.zip.gpg");
                name = name.EndsWith(".zip.gpg", StringComparison.OrdinalIgnoreCase) ? name[..^8] : Path.GetFileNameWithoutExtension(name);
                OutputPath.Text = Path.Combine(parent, string.IsNullOrWhiteSpace(name) ? "restored-folder" : name);
            }
            else
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(InputPath.Text ?? "folder"));
                using var file = await storage.SaveFilePickerAsync(new() { Title = "保存新的加密归档", SuggestedFileName = name + ".zip.gpg" });
                if (file != null) OutputPath.Text = file.TryGetLocalPath() ?? throw new IOException("请选择本地保存位置。");
            }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void Run(object? sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Progress.IsIndeterminate = true; Progress.Value = 0;
        Inputs.IsEnabled = false; CancelButton.IsEnabled = Progress.IsVisible = true;
        Status.Text = "准备中…";
        try
        {
            if (_library == null || LibraryKeys.SelectedItem is not KeySummary key) throw new ArgumentException("请先选择密钥。");
            var source = InputPath.Text?.Trim() ?? ""; var output = OutputPath.Text?.Trim() ?? "";
            if (source.Length == 0 || output.Length == 0) throw new ArgumentException("请选择输入和完整输出路径。");
            var progress = new Progress<FolderCryptoProgress>(p =>
            {
                if (!ReferenceEquals(_cancellation, cancellation) || cancellation.IsCancellationRequested) return;
                Status.Text = $"{p.Phase}：{p.Item}\n条目 {p.Completed}/{p.Total}，字节 {p.Bytes:N0}/{p.TotalBytes:N0}";
                Progress.IsIndeterminate = p.TotalBytes == 0;
                if (p.TotalBytes > 0) Progress.Value = Math.Min(100, p.Bytes * 100d / p.TotalBytes);
            });
            FolderCryptoResult result;
            if (Decrypt)
            {
                var material = await _library.GetSecretAsync(key.Id);
                result = await _service.DecryptAsync(source, output, material.Key, material.Passphrase, progress, cancellation.Token);
            }
            else
            {
                if (key.EndsOn.HasValue && key.EndsOn.Value.ToUniversalTime() <= DateTime.UtcNow) throw new InvalidOperationException("密钥已过期，请选择有效密钥。");
                var publicKey = await _library.GetPublicKeyAsync(key.Id);
                result = await _service.EncryptAsync(source, output, publicKey, progress, cancellation.Token);
            }
            Status.Text = $"成功：{result.OutputPath}\n文件 {result.Files}，目录 {result.Directories}，原始数据 {result.PlainBytes:N0} 字节。";
        }
        catch (OperationCanceledException) { Status.Text = "已取消，未发布部分输出；临时内容已清理。"; }
        catch (Exception ex) { Status.Text = $"未完成：{ex.Message}"; }
        finally
        {
            _cancellation = null; Inputs.IsEnabled = true; CancelButton.IsEnabled = Progress.IsVisible = false;
        }
    }
    private void Cancel(object? sender, RoutedEventArgs e)
    { _cancellation?.Cancel(); CancelButton.IsEnabled = false; Status.Text = "正在取消并清理…"; }
}
