using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.DuplicateFiles.Scanning;

namespace PracticalTools.Desktop.Views;

public partial class DuplicateScanPage : UserControl
{
    private IDuplicateScanner _scanner = new DuplicateScanner();
    private ScanHistoryStore? _history;
    private DuplicateScan? _scan;
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private List<ScannedFile> _visible = [];

    public DuplicateScanPage()
    {
        InitializeComponent();
        ConfigureRemovalMode();
        DetachedFromVisualTree += (_, _) => _cancellation?.Cancel();
    }
    public void Configure(IDuplicateScanner scanner, ScanHistoryStore? history = null)
    {
        _scanner = scanner; _history = history;
        ConfigureRemovalMode();
    }

    private void ConfigureRemovalMode()
    {
        RecycleChoice.IsEnabled = _scanner.CanRecycle;
        RemovalMode.SelectedIndex = _scanner.CanRecycle ? 0 : 1;
    }

    private async void PickFolders(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new()
            { Title = "选择扫描目录", AllowMultiple = true });
            var paths = new List<string>();
            foreach (var folder in folders)
            {
                using (folder) paths.Add(folder.TryGetLocalPath() ?? throw new IOException("请选择本地目录。"));
            }
            if (paths.Count > 0) Roots.Text = string.Join(Environment.NewLine, paths);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Inputs.IsEnabled = HistoryButton.IsEnabled = !busy;
        Actions.IsEnabled = !busy && _scan != null;
        Results.IsEnabled = !busy;
    }

    private async void Start(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        _scan = null;
        Actions.IsEnabled = false;
        Results.ItemsSource = _visible = [];
        Warnings.Text = "";
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Progress.IsVisible = CancelButton.IsEnabled = true;
        try
        {
            var roots = (Roots.Text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var progress = new Progress<ScanProgress>(p =>
            {
                if (!ReferenceEquals(_cancellation, cancellation) || cancellation.IsCancellationRequested) return;
                Status.Text = $"{p.Phase}：已发现 {p.Discovered} 个文件，已比较 {p.Hashed}/{p.Candidates}，读取 {p.BytesRead:N0}/{p.TotalBytes:N0} 字节。";
            });
            var completed = await _scanner.ScanAsync(roots, Extensions.Text ?? "", progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            CancelButton.IsEnabled = false;
            _scan = completed;
            ApplyFilter();
            var groups = completed.Duplicates.Select(f => f.Sha256).Distinct().Count();
            Status.Text = $"完成：{completed.Files.Count} 个文件，{groups} 组重复、{completed.Duplicates.Count} 条结果；按路径计重复 {completed.DuplicateBytes:N0} 字节，跳过 {completed.Skipped} 项。";
            Warnings.Text = string.Join("\n", completed.Warnings.Take(20).Select(w => $"{w.Path}：{w.Reason}"));
            if (_history != null)
            {
                try { await _history.SaveAsync(completed); Status.Text += " 扫描记录已保存。"; }
                catch (Exception ex) { Status.Text += $" 记录保存失败：{ex.Message}"; }
            }
        }
        catch (OperationCanceledException) { Status.Text = "扫描已取消，未发布部分结果、未保存记录。"; }
        catch (Exception ex) { Status.Text = $"扫描失败：{ex.Message}"; }
        finally
        {
            _cancellation = null;
            CancelButton.IsEnabled = Progress.IsVisible = false;
            SetBusy(false);
        }
    }

    private void Cancel(object? sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        Status.Text = "正在取消扫描…";
    }

    private void Filter(object? sender, TextChangedEventArgs e) { if (Results != null) ApplyFilter(); }
    private void ApplyFilter()
    {
        var term = Search.Text?.Trim() ?? "";
        _visible = (_scan?.Duplicates ?? []).Where(f => f.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        Results.ItemsSource = _visible;
    }

    private void Suggest(object? sender, RoutedEventArgs e)
    {
        Results.SelectedItems.Clear();
        foreach (var file in DuplicateScan.SuggestRemovals(_visible)) Results.SelectedItems.Add(file);
        Status.Text = $"已选择 {Results.SelectedItems.Count} 项，优先保留创建时间较早、路径较浅的一份；尚未执行移除。";
    }

    private async void Remove(object? sender, RoutedEventArgs e)
    {
        if (_busy || _scan == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var selected = Results.SelectedItems.OfType<ScannedFile>().ToList();
        if (selected.Count == 0) { Status.Text = "请先选择文件。"; return; }
        var mode = RemovalMode.SelectedIndex == 0 ? FileRemovalMode.Recycle : FileRemovalMode.Permanent;
        var action = mode == FileRemovalMode.Recycle ? "移入 Windows 回收站" : "永久删除（不可恢复）";
        SetBusy(true);
        try
        {
            var message = $"即将{action} {selected.Count} 个文件。\n每组必须保留至少一份；确认后仍会重新校验内容。\n\n" +
                string.Join("\n", selected.Select(f => f.FilePath));
            if (!await ConfirmationWindow.AskAsync(owner, message)) { Status.Text = "已取消移除，没有改动文件。"; return; }
            Status.Text = $"正在复查并{action}…";
            Progress.IsVisible = true;
            var result = await _scanner.RemoveAsync(_scan, selected, mode);
            var removed = result.Outcomes.Where(o => o.Success).Select(o => o.Path).ToList();
            _scan = _scan.Without(removed);
            ApplyFilter();
            Status.Text = $"已移除 {removed.Count}，未移除 {result.Outcomes.Count - removed.Count}。" + (result.Cancelled ? "用户取消了后续处理。" : "");
            Warnings.Text = string.Join("\n", result.Outcomes.Where(o => !o.Success).Select(o => $"{o.Path}：{o.Error}"));
        }
        catch (Exception ex) { Status.Text = $"未完成移除：{ex.Message}"; }
        finally { Progress.IsVisible = false; SetBusy(false); }
    }

    private async void LoadHistory(object? sender, RoutedEventArgs e)
    {
        if (_busy || _history == null) return;
        HistoryButton.IsEnabled = false;
        try { History.ItemsSource = await _history.RecentAsync(); }
        catch (Exception ex) { Status.Text = $"加载记录失败：{ex.Message}"; }
        finally { HistoryButton.IsEnabled = !_busy; }
    }
}
