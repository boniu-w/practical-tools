using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;
using PracticalTools.GPG.Preview;
using SkiaSharp;
using PracticalTools.Desktop.Preview;

namespace PracticalTools.Desktop.Views;

public partial class DecryptionPreviewPage : UserControl
{
    private KeyLibrary? _library;
    private PreviewService? _service;
    private Task? _reload;
    private CancellationTokenSource? _cancellation;
    private Bitmap? _bitmap;
    private readonly PdfRenderer _pdfRenderer = new();
    private byte[]? _pdf;
    private int _pdfPage, _pdfCount, _pdfZoom = 100;
    public DecryptionPreviewPage()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) => await ReloadKeysAsync();
        DetachedFromVisualTree += (_, _) => Release();
        InputPath.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Release(); };
        LibraryKeys.SelectionChanged += (_, _) => Release();
    }
    public void Configure(KeyLibrary library, string workspaceParent)
    { _library = library; _service = new PreviewService(workspaceParent); }
    public Task ReloadKeysAsync() => _reload is { IsCompleted: false } ? _reload : _reload = ReloadCoreAsync();
    private async Task ReloadCoreAsync()
    {
        if (_library == null) return;
        try { LibraryKeys.ItemsSource = (await _library.ListAsync()).Where(k => k.HasSecret).ToList(); }
        catch (Exception ex) { Status.Text = $"加载密钥失败：{ex.Message}"; }
    }
    private async void RefreshKeys(object? sender, RoutedEventArgs e) => await ReloadKeysAsync();
    private async void PickInput(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new() { Title = "选择加密文件", AllowMultiple = false });
            if (files.Count == 0) return;
            using var file = files[0]; InputPath.Text = file.TryGetLocalPath() ?? throw new IOException("请选择本地文件。");
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private async void Run(object? sender, RoutedEventArgs e)
    {
        if (_cancellation != null) return;
        Release();
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; Inputs.IsEnabled = false; Status.Text = "正在解密并校验…";
        try
        {
            if (_library == null || _service == null || LibraryKeys.SelectedItem is not KeySummary key)
                throw new ArgumentException("请先选择包含私钥的密钥。");
            var path = InputPath.Text?.Trim() ?? "";
            var material = await _library.GetSecretAsync(key.Id);
            using var content = await _service.OpenAsync(path, material.Key, material.Passphrase, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (content.Media != null)
            {
                var extension = Path.GetExtension(content.Name).ToLowerInvariant();
                MediaView.IsVisible = true;
                MediaView.Open(content.Media, content.MediaDemux!, extension is ".mp4" or ".m4v" or ".mov" or ".mkv" or ".webm" or ".avi" or ".ogv");
            }
            else if (content.Pdf != null)
            {
                _pdf = content.Pdf.ToArray();
                await ShowPdfPageAsync(0, 100, cancellation.Token);
            }
            else if (content.Sheets != null)
            {
                SheetSelector.ItemsSource = content.Sheets;
                WorkbookHeader.IsVisible = true;
                SheetSelector.SelectedIndex = 0;
            }
            else if (content.Image != null)
            {
                using var stream = new MemoryStream(content.Image, writable: false);
                using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("图片格式无效。");
                var info = codec.Info;
                if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 16_000_000)
                    throw new InvalidDataException("图片超过 1600 万像素预览限制。");
                using var bitmapStream = new MemoryStream(content.Image, writable: false);
                _bitmap = Bitmap.DecodeToWidth(bitmapStream, Math.Min(1600, info.Width));
                ImagePreview.Source = _bitmap; ImageHost.IsVisible = true;
            }
            else { TextPreview.Text = content.Text; TextPreview.IsVisible = true; }
            Status.Text = $"{content.Name} — 临时明文已清理。" + (content.Truncated ?
                content.Sheets == null ? "仅显示前 20 万字符。" : "工作簿超过预览范围或容量限制，部分内容未显示。" : "");
        }
        catch (OperationCanceledException) { Status.Text = "已取消预览。"; }
        catch (Exception ex) { ClearContent(); Status.Text = $"无法预览：{ex.Message}"; }
        finally { _cancellation = null; Inputs.IsEnabled = true; UpdatePdfControls(); }
    }
    private void ClearContent()
    {
        MediaView.Close(); MediaView.IsVisible = false;
        if (_pdf != null) Array.Clear(_pdf);
        _pdf = null; _pdfPage = _pdfCount = 0; _pdfZoom = 100;
        PdfControls.IsVisible = false; PdfPosition.Text = PdfZoom.Text = null;
        ImagePreview.Width = ImagePreview.Height = double.NaN;
        SheetSelector.ItemsSource = null; WorkbookHeader.IsVisible = false;
        TextPreview.Text = null; TextPreview.IsVisible = ImageHost.IsVisible = false;
        ImagePreview.Source = null; _bitmap?.Dispose(); _bitmap = null;
    }
    private async Task ShowPdfPageAsync(int page, int zoom, CancellationToken token)
    {
        using var rendered = await _pdfRenderer.RenderAsync(_pdf ?? throw new InvalidOperationException("PDF 已关闭。"), page, zoom, token);
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(rendered.Png, writable: false);
        var bitmap = new Bitmap(stream);
        ImagePreview.Source = bitmap; _bitmap?.Dispose(); _bitmap = bitmap;
        ImagePreview.Width = rendered.Width; ImagePreview.Height = rendered.Height;
        ImageHost.Offset = default; ImageHost.IsVisible = PdfControls.IsVisible = true;
        _pdfPage = page; _pdfCount = rendered.PageCount; _pdfZoom = zoom;
        PdfPosition.Text = $"{page + 1} / {_pdfCount}"; PdfZoom.Text = $"{zoom}%";
    }
    private void UpdatePdfControls()
    {
        PdfControls.IsEnabled = _cancellation == null;
        PreviousPage.IsEnabled = _pdfPage > 0; NextPage.IsEnabled = _pdfPage + 1 < _pdfCount;
        ZoomOut.IsEnabled = _pdfZoom > 50; ZoomIn.IsEnabled = _pdfZoom < 200;
    }
    private async Task NavigatePdfAsync(int page, int zoom)
    {
        if (_pdf == null || _cancellation != null) return;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; Inputs.IsEnabled = false; UpdatePdfControls();
        Status.Text = "正在渲染 PDF 页面…";
        try
        {
            await ShowPdfPageAsync(page, zoom, cancellation.Token);
            Status.Text = "PDF 页面已显示；临时明文已清理。";
        }
        catch (OperationCanceledException) { Status.Text = "已取消预览。"; }
        catch (Exception ex) { Status.Text = $"无法显示该页面：{ex.Message}"; }
        finally { _cancellation = null; Inputs.IsEnabled = true; UpdatePdfControls(); }
    }
    private async void PreviousPdfPage(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage - 1, _pdfZoom);
    private async void NextPdfPage(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage + 1, _pdfZoom);
    private async void ZoomPdfOut(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage, _pdfZoom - 25);
    private async void ZoomPdfIn(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage, _pdfZoom + 25);
    private void SelectSheet(object? sender, SelectionChangedEventArgs e)
    {
        TextPreview.Text = (SheetSelector.SelectedItem as PreviewSheet)?.Text;
        TextPreview.IsVisible = SheetSelector.SelectedItem is PreviewSheet;
    }
    private void Release() { _cancellation?.Cancel(); ClearContent(); Status.Text = "预览已关闭。"; }
    private void ClosePreview(object? sender, RoutedEventArgs e) => Release();
}
