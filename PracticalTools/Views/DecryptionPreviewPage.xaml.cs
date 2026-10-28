using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using PracticalTools.GPG;
using PracticalTools.vo;

namespace PracticalTools.Views;

public partial class DecryptionPreviewPage : UserControl
{
    private const string PreviewOrigin = "https://preview.invalid/";
    private WebView2? _browser;
    private DecryptedPreviewSession? _session;
    private CancellationTokenSource? _previewCts;
    private Task _cleanup = Task.CompletedTask;
    private Task _browserExited = Task.CompletedTask;
    private bool _busy;
    private bool _expanded;
    public event Action<bool>? ExpandedChanged;

    public DecryptionPreviewPage()
    {
        InitializeComponent();
        Application.Current.Exit += Application_Exit;
    }

    public void SetEncryptedFile(string path)
    {
        _previewCts?.Cancel();
        if (!_busy) _ = ReleasePreviewAsync();
        EncryptedPathTextBox.Text = path;
        StatusText.Text = "已选择加密文件，点击“查看加密文件”即可查看内容。";
    }

    private void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要查看的加密文件",
            Filter = "GPG 文件 (*.gpg)|*.gpg|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) SetEncryptedFile(dialog.FileName);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || DataContext is not MainViewModel vm) return;
        var selected = vm.GpgFileWrapperList?.Where(x => x.IsSelected).ToList();
        if (selected == null || selected.Count != 1)
        {
            SetStatus("请在下方勾选一个密钥，查看加密文件只能使用一个密钥。", true);
            return;
        }
        if (string.IsNullOrWhiteSpace(selected[0].GpgFile.SecretKey))
        {
            SetStatus("选中的密钥只有公钥，无法解密。请选择包含私钥的密钥。", true);
            return;
        }
        string encryptedPath = EncryptedPathTextBox.Text;
        if (!File.Exists(encryptedPath))
        {
            SetStatus("请先选择一个存在的加密文件。", true);
            return;
        }

        SetBusy(true);
        _previewCts?.Dispose();
        var cts = _previewCts = new CancellationTokenSource();
        DecryptedPreviewSession? pendingSession = null;
        try
        {
            await ReleasePreviewAsync();
            cts.Token.ThrowIfCancellationRequested();
            // 先检查运行环境，缺少组件时不产生解密副本。
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            SetStatus("正在解密，请稍候；可点击“关闭预览”取消。");
            pendingSession = await DecryptedPreviewSession.CreateAsync(encryptedPath,
                selected[0].GpgFile, cts.Token);
            string target;
            if (Path.GetExtension(pendingSession.FilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                target = PreviewOrigin + Path.GetFileName(pendingSession.FilePath);
            else
            {
                var session = pendingSession;
                string html = await Task.Run(() => PreviewContentBuilder.Build(session.FilePath,
                    session.OriginalFileName, cts.Token), cts.Token);
                string htmlPath = Path.Combine(session.ContentDirectory, "index.html");
                await File.WriteAllTextAsync(htmlPath, html, cts.Token);
                target = PreviewOrigin + "index.html";
            }
            cts.Token.ThrowIfCancellationRequested();
            _session = pendingSession;
            pendingSession = null;
            var browser = new WebView2
            {
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(_session.DirectoryPath, "browser"),
                    IsInPrivateModeEnabled = true
                }
            };
            _browser = browser;
            BrowserHost.Children.Add(browser);
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var environment = core.Environment;
            EventHandler<CoreWebView2BrowserProcessExitedEventArgs>? onExited = null;
            onExited = (_, _) =>
            {
                environment.BrowserProcessExited -= onExited;
                exited.TrySetResult(true);
            };
            environment.BrowserProcessExited += onExited;
            _browserExited = exited.Task;
            cts.Token.ThrowIfCancellationRequested();
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.SetVirtualHostNameToFolderMapping("preview.invalid", _session.ContentDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) =>
            {
                if (!args.Uri.StartsWith(PreviewOrigin, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) &&
                    (uri.Scheme is "http" or "https") && uri.Host != "preview.invalid")
                    args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (_browser != browser) return;
                SetStatus(args.IsSuccess ? $"正在预览：{_session?.OriginalFileName}" :
                    $"预览加载失败：{args.WebErrorStatus}。请确认文件内容或格式有效。", !args.IsSuccess);
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (_browser == browser && args.WebMessageAsJson == "\"media-error\"")
                    SetStatus("媒体无法播放或图片无法加载。文件可能损坏，或当前系统不支持此编码。可使用“我要解密”保存后查看。", true);
            };
            EmptyHint.Visibility = Visibility.Collapsed;
            SetStatus($"解密完成，正在加载：{_session.OriginalFileName}");
            core.Navigate(target);
        }
        catch (OperationCanceledException)
        {
            await ReleasePreviewAsync();
            SetStatus("预览已取消。");
        }
        catch (Exception ex)
        {
            await ReleasePreviewAsync();
            if (!cts.IsCancellationRequested)
                SetStatus(ex is WebView2RuntimeNotFoundException
                    ? "预览需要 Microsoft Edge WebView2 Runtime，请安装后重新预览。"
                    : $"查看加密文件失败：{ex.Message}", true);
        }
        finally
        {
            if (pendingSession != null) await Task.Run(pendingSession.Dispose);
            SetBusy(false);
        }
    }

    private async void ClosePreview_Click(object sender, RoutedEventArgs e)
    {
        _previewCts?.Cancel();
        if (_busy) return; // 正在解密/初始化时由 Preview_Click 的 finally 统一清理。
        await ReleasePreviewAsync();
        SetStatus("预览已关闭。");
    }

    private async Task ReleasePreviewAsync()
    {
        _browser?.Dispose();
        _browser = null;
        BrowserHost.Children.Clear();
        EmptyHint.Visibility = Visibility.Visible;
        var session = _session;
        _session = null;
        var browserExited = _browserExited;
        _browserExited = Task.CompletedTask;
        if (session != null)
        {
            var previous = _cleanup;
            _cleanup = Task.Run(async () =>
            {
                await previous;
                session.DeleteDecryptedContent();
            });
            // 先释放明文，浏览器配置目录在进程退出后清理，不阻塞下一次查看。
            _ = FinishCleanupAsync(session, browserExited, _cleanup);
        }
        await _cleanup;
    }

    private static async Task FinishCleanupAsync(DecryptedPreviewSession session, Task browserExited, Task contentCleanup)
    {
        await contentCleanup;
        await Task.WhenAny(browserExited, Task.Delay(TimeSpan.FromSeconds(8)));
        await Task.Run(session.Dispose);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PreviewButton.IsEnabled = SelectFileButton.IsEnabled = !busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error ? Brushes.Firebrick : Brushes.SlateGray;
        if (DataContext is MainViewModel vm)
        {
            vm.GpgStatusMessage = message;
            vm.GpgStatusColor = new SolidColorBrush(error ? Colors.Red : Colors.ForestGreen);
        }
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        ExpandButton.Content = _expanded ? "显示密钥" : "放大";
        ExpandedChanged?.Invoke(_expanded);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => ExpandedChanged?.Invoke(_expanded);

    private async void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _previewCts?.Cancel();
        ExpandedChanged?.Invoke(false);
        if (!_busy) await ReleasePreviewAsync();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _previewCts?.Cancel();
        _browser?.Dispose();
        _browser = null;
        DecryptedPreviewSession.CleanupAll();
    }
}
