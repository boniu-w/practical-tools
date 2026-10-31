using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Labs.Gif;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;
using PracticalTools.GPG.Preview;
using PracticalTools.Desktop.Preview;

namespace PracticalTools.Desktop.Views;

public partial class DecryptionPreviewPage : UserControl
{
    private KeyLibrary? _library;
    private PreviewService? _service;
    private Task? _reload;                 // 正在进行中的密钥加载任务，用于避免重复查询。
    private CancellationTokenSource? _cancellation; // 非空表示解密或渲染任务正在运行。
    private Bitmap? _bitmap;
    private Stream? _gifStream;
    private GifImage? _gifView;
    private readonly PdfRenderer _pdfRenderer = new();
    private byte[]? _pdf;                  // 解密后的 PDF 明文，关闭预览时清零。
    private int _pdfPage, _pdfCount, _pdfZoom = 100;

    /// <summary>初始化控件，并在挂载/卸载、输入路径或密钥选择变化时加载密钥或释放预览。</summary>
    public DecryptionPreviewPage()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) => await ReloadKeysAsync();
        DetachedFromVisualTree += (_, _) => Release();
        InputPath.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Release(); };
        LibraryKeys.SelectionChanged += (_, _) => Release();
    }
    /// <summary>注入密钥库，并为工作区创建预览服务。</summary>
    /// <param name="library">提供私钥材料的密钥库。</param>
    /// <param name="workspaceParent">临时解密文件的工作区父目录。</param>
    public void Configure(KeyLibrary library, string workspaceParent)
    { _library = library; _service = new PreviewService(workspaceParent); }

    /// <summary>加载私钥列表；已有加载任务进行中时复用该任务，避免重复查询。</summary>
    public Task ReloadKeysAsync() => _reload is { IsCompleted: false } ? _reload : _reload = ReloadCoreAsync();

    /// <summary>查询密钥库并仅显示包含私钥的密钥。</summary>
    private async Task ReloadCoreAsync()
    {
        if (_library == null) return;
        try { LibraryKeys.ItemsSource = (await _library.ListAsync()).Where(k => k.HasSecret).ToList(); }
        catch (Exception ex) { UiStatus.Set(Status, $"加载密钥失败：{ex.Message}", UiStatusKind.Error); }
    }
    /// <summary>刷新密钥列表的事件处理。</summary>
    private async void RefreshKeys(object? sender, RoutedEventArgs e) => await ReloadKeysAsync();

    /// <summary>打开文件选择器，并把选中的本地文件路径填入输入框。</summary>
    private async void PickInput(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new() { Title = "选择加密文件", AllowMultiple = false });
            if (files.Count == 0) return;
            using var file = files[0]; InputPath.Text = file.TryGetLocalPath() ?? throw new IOException("请选择本地文件。");
        }
        catch (Exception ex) { UiStatus.Set(Status, ex.Message, UiStatusKind.Error); }
    }
    /// <summary>
    /// 解密所选文件并预览：按内容分派到媒体、PDF、工作簿、图片或文本视图。
    /// 运行期间禁用输入；结束后清理临时明文并恢复控件状态。
    /// </summary>
    private async void Run(object? sender, RoutedEventArgs e)
    {
        // 同一时间只允许一个解密/预览任务。
        if (_cancellation != null) return;
        Release();
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; Inputs.IsEnabled = false;
        UiStatus.Set(Status, "正在解密并校验…", UiStatusKind.Busy);
        string? imageNotice = null;
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
                MediaView.Open(content.Media, content.MediaDemux!, extension is ".mp4" or ".m4v" or ".mov" or ".mkv" or ".webm" or ".avi" or ".ogv" or ".wmv");
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
                var extension = Path.GetExtension(content.Name).ToLowerInvariant();
                var image = await Task.Run(() => PreviewImageLoader.Load(content.Image, extension, cancellation.Token),
                    cancellation.Token);
                imageNotice = image.Notice;
                // 动画 GIF 交给专用控件播放，静态图片直接设置位图。
                if (image.IsAnimated)
                {
                    var stream = image.GifStream!;
                    _gifStream = stream;
                    _gifView = new GifImage
                    {
                        Source = stream,
                        Stretch = Stretch.Uniform,
                        Width = image.Size.Width,
                        Height = image.Size.Height
                    };
                    GifHost.Children.Add(_gifView);
                    GifHost.IsVisible = true;
                    ImagePreview.Source = null;
                    ImageHost.IsVisible = true;
                }
                else
                {
                    _bitmap = image.Bitmap ?? throw new InvalidDataException("图片格式无效。");
                    ImagePreview.Source = _bitmap;
                    ImagePreview.Width = ImagePreview.Height = double.NaN;
                    GifHost.Children.Clear();
                    GifHost.IsVisible = false;
                    ImageHost.IsVisible = true;
                }
            }
            // 其余内容按纯文本显示。
            else { TextPreview.Text = content.Text; TextPreview.IsVisible = true; }
            PreviewPlaceholder.IsVisible = false;
            UiStatus.Set(Status, $"{content.Name} — 临时明文已清理。" + (content.Truncated ?
                content.Sheets == null ? "仅显示前 20 万字符。" : "工作簿超过预览范围或容量限制，部分内容未显示。" : "")
                + (imageNotice is null ? "" : " " + imageNotice), UiStatusKind.Success);
        }
        catch (OperationCanceledException) { UiStatus.Set(Status, "已取消预览。"); }
        catch (Exception ex) { ClearContent(); UiStatus.Set(Status, $"无法预览：{ex.Message}", UiStatusKind.Error); }
        finally { _cancellation = null; Inputs.IsEnabled = true; UpdatePdfControls(); }
    }
    /// <summary>释放图片、GIF、PDF 等预览资源，清零 PDF 明文并重置相关状态。</summary>
    private void ClearContent()
    {
        MediaView.Close(); MediaView.IsVisible = false;
        GifHost.Children.Clear(); GifHost.IsVisible = false; _gifView = null;
        _gifStream?.Dispose(); _gifStream = null;
        // 清零解密后的 PDF 明文。
        if (_pdf != null) Array.Clear(_pdf);
        _pdf = null; _pdfPage = _pdfCount = 0; _pdfZoom = 100;
        PdfControls.IsVisible = false; PdfPosition.Text = PdfZoom.Text = null;
        ImagePreview.Width = ImagePreview.Height = double.NaN;
        SheetSelector.ItemsSource = null; WorkbookHeader.IsVisible = false;
        TextPreview.Text = null; TextPreview.IsVisible = ImageHost.IsVisible = false;
        ImagePreview.Source = null; _bitmap?.Dispose(); _bitmap = null;
        PreviewPlaceholder.IsVisible = true;
    }
    /// <summary>渲染指定 PDF 页并替换当前位图，同时更新页码与缩放显示。</summary>
    /// <param name="page">从 0 开始的页码。</param>
    /// <param name="zoom">缩放百分比。</param>
    /// <param name="token">取消标记。</param>
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
    /// <summary>按运行状态、当前页码和缩放值更新 PDF 控件的可用状态。</summary>
    private void UpdatePdfControls()
    {
        PdfControls.IsEnabled = _cancellation == null;
        PreviousPage.IsEnabled = _pdfPage > 0; NextPage.IsEnabled = _pdfPage + 1 < _pdfCount;
        ZoomOut.IsEnabled = _pdfZoom > 50; ZoomIn.IsEnabled = _pdfZoom < 200;
    }
    /// <summary>切换 PDF 页码或缩放；已有任务正在运行时忽略本次操作。</summary>
    private async Task NavigatePdfAsync(int page, int zoom)
    {
        if (_pdf == null || _cancellation != null) return;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; Inputs.IsEnabled = false; UpdatePdfControls();
        UiStatus.Set(Status, "正在渲染 PDF 页面…", UiStatusKind.Busy);
        try
        {
            await ShowPdfPageAsync(page, zoom, cancellation.Token);
            UiStatus.Set(Status, "PDF 页面已显示；临时明文已清理。", UiStatusKind.Success);
        }
        catch (OperationCanceledException) { UiStatus.Set(Status, "已取消预览。"); }
        catch (Exception ex) { UiStatus.Set(Status, $"无法显示该页面：{ex.Message}", UiStatusKind.Error); }
        finally { _cancellation = null; Inputs.IsEnabled = true; UpdatePdfControls(); }
    }
    /// <summary>转到上一页。</summary>
    private async void PreviousPdfPage(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage - 1, _pdfZoom);

    /// <summary>转到下一页。</summary>
    private async void NextPdfPage(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage + 1, _pdfZoom);

    /// <summary>缩小 25 个百分点。</summary>
    private async void ZoomPdfOut(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage, _pdfZoom - 25);

    /// <summary>放大 25 个百分点。</summary>
    private async void ZoomPdfIn(object? sender, RoutedEventArgs e) => await NavigatePdfAsync(_pdfPage, _pdfZoom + 25);

    /// <summary>显示所选工作表的文本；未选择时隐藏文本预览。</summary>
    private void SelectSheet(object? sender, SelectionChangedEventArgs e)
    {
        TextPreview.Text = (SheetSelector.SelectedItem as PreviewSheet)?.Text;
        TextPreview.IsVisible = SheetSelector.SelectedItem is PreviewSheet;
    }
    /// <summary>取消正在进行的任务并释放全部预览内容。</summary>
    private void Release() { _cancellation?.Cancel(); ClearContent(); UiStatus.Set(Status, "预览已关闭。"); }

    /// <summary>关闭预览的事件处理。</summary>
    private void ClosePreview(object? sender, RoutedEventArgs e) => Release();
}
