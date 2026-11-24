using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.DuplicateFiles.Scanning;

namespace PracticalTools.Desktop.Views;

public partial class DuplicateScanPage : UserControl
{
    private readonly ObservableCollection<string> _selectedExtensions = [];
    private readonly Dictionary<string, CheckBox> _extensionOptions = new(StringComparer.OrdinalIgnoreCase);
    private IDuplicateScanner _scanner = new DuplicateScanner();
    private ScanHistoryStore? _history;
    private DuplicateScan? _scan;          // 当前扫描结果。
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private List<ScannedFile> _visible = []; // 当前筛选后显示的结果。

    /// <summary>初始化控件；离开界面时取消仍在运行的扫描。</summary>
    public DuplicateScanPage()
    {
        InitializeComponent();
        ExtensionTags.ItemsSource = _selectedExtensions;
        foreach (var extension in ".txt .pdf .doc .docx .xls .xlsx .ppt .pptx .csv .jpg .jpeg .png .gif .webp .mp3 .wav .mp4 .mkv .zip .rar .7z".Split(' '))
            GetExtensionOption(extension);
        Results.AddHandler(PointerPressedEvent, FitColumnOnDoubleClick, RoutingStrategies.Tunnel);
        Results.LayoutUpdated += FillPathColumn;
        ConfigureRemovalMode();
        DetachedFromVisualTree += (_, _) => _cancellation?.Cancel();
    }

    private void OpenExtensionList(object? sender, RoutedEventArgs e) => ExtensionPopup.IsOpen = true;

    private void ExtensionTagsPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Avalonia.Visual source && source is not Button &&
            source.FindAncestorOfType<Button>() == null && e.GetCurrentPoint(ExtensionTags).Properties.IsLeftButtonPressed)
            ExtensionPopup.IsOpen = true;
    }

    private CheckBox GetExtensionOption(string extension)
    {
        if (_extensionOptions.TryGetValue(extension, out var existing)) return existing;

        var option = new CheckBox { Content = extension, MinWidth = 78 };
        option.IsCheckedChanged += (_, _) =>
        {
            if (option.IsChecked == true) _selectedExtensions.Add(extension);
            else _selectedExtensions.Remove(extension);
            ExtensionHint.IsVisible = _selectedExtensions.Count == 0;
            ClearExtensionsButton.IsEnabled = _selectedExtensions.Count > 0;
        };
        _extensionOptions.Add(extension, option);
        ExtensionOptions.Children.Add(option);
        return option;
    }

    private void ClearExtensions(object? sender, RoutedEventArgs e)
    {
        foreach (var extension in _selectedExtensions.ToArray())
            _extensionOptions[extension].IsChecked = false;
    }

    private void RemoveExtension(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string extension }) _extensionOptions[extension].IsChecked = false;
        e.Handled = true;
    }

    private void CustomExtensionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddCustomExtensions(sender, e);
        e.Handled = true;
    }

    private void AddCustomExtensions(object? sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var extension in DuplicateScanner.ParseExtensions(CustomExtensions.Text ?? ""))
                GetExtensionOption(extension).IsChecked = true;
            CustomExtensions.Clear();
            ExtensionError.IsVisible = false;
        }
        catch (ArgumentException ex)
        {
            ExtensionError.Text = ex.Message;
            ExtensionError.IsVisible = true;
        }
    }

    // 用表头的实际可视宽度作为基准，扣除其他列后，剩余空间由路径列占满。
    private void FillPathColumn(object? sender, EventArgs e)
    {
        var headers = Results.GetVisualDescendants().OfType<DataGridColumnHeadersPresenter>().FirstOrDefault();
        if (headers == null || headers.Bounds.Width <= 0) return;

        var pathColumn = Results.Columns[3];
        var otherWidth = Results.Columns.Where(c => c != pathColumn && c.IsVisible).Sum(c => c.ActualWidth);
        var minWidth = Math.Max(Results.MinColumnWidth, headers.Bounds.Width - otherWidth);
        if (Math.Abs(pathColumn.MinWidth - minWidth) > 0.01)
            pathColumn.MinWidth = minWidth;
    }
    private void FitColumnOnDoubleClick(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || !e.GetCurrentPoint(Results).Properties.IsLeftButtonPressed ||
            e.Source is not Avalonia.Visual source) return;

        var header = source as DataGridColumnHeader ?? source.FindAncestorOfType<DataGridColumnHeader>();
        if (header == null) return;

        var column = Results.Columns.FirstOrDefault(c => Equals(c.Header, header.Content));
        if (column == null) return;

        const double edgeWidth = 5;
        var x = e.GetPosition(header).X;
        if (x <= edgeWidth)
            column = Results.Columns.Where(c => c.IsVisible && c.DisplayIndex < column.DisplayIndex)
                .MaxBy(c => c.DisplayIndex);
        else if (header.Bounds.Width - x > edgeWidth)
            return;

        if (column == null) return;
        column.Width = DataGridLength.Auto;
        e.Handled = true;
    }

    /// <summary>注入查重扫描器和可选的扫描记录存储。</summary>
    /// <param name="scanner">执行扫描与移除的扫描器实现。</param>
    /// <param name="history">扫描记录存储；为 null 时不保存历史。</param>
    public void Configure(IDuplicateScanner scanner, ScanHistoryStore? history = null)
    {
        _scanner = scanner; _history = history;
        ConfigureRemovalMode();
    }

    /// <summary>按扫描器是否支持回收站来启用移除方式，并选择默认方式。</summary>
    private void ConfigureRemovalMode()
    {
        RecycleChoice.IsEnabled = _scanner.CanRecycle;
        RemovalMode.SelectedIndex = _scanner.CanRecycle ? 0 : 1;
    }

    /// <summary>选择多个扫描目录并填入输入框，每行一个路径。</summary>
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
            if (paths.Count == 0) return;

            var existing = (Roots.Text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            Roots.Text = string.Join(Environment.NewLine, existing.Union(paths, comparer));
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    /// <summary>扫描或移除期间禁用输入与结果操作，并同步按钮状态。</summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;
        Inputs.IsEnabled = HistoryButton.IsEnabled = !busy;
        Actions.IsEnabled = !busy && _scan != null;
        Results.IsEnabled = !busy;
    }

    /// <summary>
    /// 按输入的目录和扩展名启动查重扫描，并实时显示进度。
    /// 完成后应用筛选并可保存扫描记录；取消时丢弃部分结果且不保存记录。
    /// </summary>
    private async void Start(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        _scan = null;
        Actions.IsEnabled = false;
        Results.ItemsSource = _visible = [];
        ShowEmpty("还没有扫描结果", "设置目录和后缀后开始扫描。");
        Warnings.Text = "";
        UiStatus.Set(Status, "正在扫描…", UiStatusKind.Busy);
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Progress.IsVisible = CancelButton.IsEnabled = true;
        try
        {
            var roots = (Roots.Text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var progress = new Progress<ScanProgress>(p =>
            {
                // 只让当前扫描任务更新进度；已取消或已被替换时忽略回传。
                if (!ReferenceEquals(_cancellation, cancellation) || cancellation.IsCancellationRequested) return;
                Status.Text = $"{p.Phase}：已发现 {p.Discovered} 个文件，已比较 {p.Hashed}/{p.Candidates}，读取 {p.BytesRead:N0}/{p.TotalBytes:N0} 字节。";
            });
            var completed = await _scanner.ScanAsync(roots, string.Join(";", _selectedExtensions), progress, cancellation.Token,
                excludeSelectedExtensions: ExcludeExtensions.IsChecked == true);
            cancellation.Token.ThrowIfCancellationRequested();
            CancelButton.IsEnabled = false;
            _scan = completed;
            ApplyFilter();
            var groups = completed.Duplicates.Select(f => f.Sha256).Distinct().Count();
            UiStatus.Set(Status, $"完成：{completed.Files.Count} 个文件，{groups} 组重复、{completed.Duplicates.Count} 条结果；按路径计重复 {completed.DuplicateBytes:N0} 字节，跳过 {completed.Skipped} 项。", UiStatusKind.Success);
            Warnings.Text = string.Join("\n", completed.Warnings.Take(20).Select(w => $"{w.Path}：{w.Reason}"));
            if (_history != null)
            {
                try { await _history.SaveAsync(completed); Status.Text += " 扫描记录已保存。"; }
                catch (Exception ex) { UiStatus.Set(Status, Status.Text + $" 记录保存失败：{ex.Message}", UiStatusKind.Error); }
            }
        }
        catch (OperationCanceledException) { UiStatus.Set(Status, "扫描已取消，未发布部分结果、未保存记录。"); }
        catch (Exception ex) { UiStatus.Set(Status, $"扫描失败：{ex.Message}", UiStatusKind.Error); }
        finally
        {
            _cancellation = null;
            CancelButton.IsEnabled = Progress.IsVisible = false;
            SetBusy(false);
        }
    }

    /// <summary>请求取消正在运行的扫描。</summary>
    private void Cancel(object? sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        UiStatus.Set(Status, "正在取消扫描…", UiStatusKind.Busy);
    }

    /// <summary>筛选条件变化时重新应用筛选。</summary>
    private void Filter(object? sender, TextChangedEventArgs e) { if (Results != null) ApplyFilter(); }

    /// <summary>按路径关键字（不区分大小写）筛选当前扫描的重复文件。</summary>
    private void ApplyFilter()
    {
        var term = Search.Text?.Trim() ?? "";
        _visible = (_scan?.Duplicates ?? []).Where(f => f.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        Results.ItemsSource = _visible;
        if (_scan == null) ShowEmpty("还没有扫描结果", "设置目录和后缀后开始扫描。");
        else if (_visible.Count == 0)
            ShowEmpty(term.Length == 0 ? "没有发现重复文件" : "没有匹配的筛选结果",
                term.Length == 0 ? "该扫描没有找到内容相同的文件。" : "尝试更换筛选关键词。");
        else EmptyResults.IsVisible = false;
    }

    private void ShowEmpty(string title, string hint)
    {
        EmptyResultsTitle.Text = title;
        EmptyResultsHint.Text = hint;
        EmptyResults.IsVisible = true;
    }

    /// <summary>
    /// 自动勾选建议移除的文件：每组优先保留创建时间较早、路径较浅的一份。
    /// 只更改选择，不执行任何删除。
    /// </summary>
    private void Suggest(object? sender, RoutedEventArgs e)
    {
        Results.SelectedItems.Clear();
        foreach (var file in DuplicateScan.SuggestRemovals(_visible)) Results.SelectedItems.Add(file);
        UiStatus.Set(Status, $"已选择 {Results.SelectedItems.Count} 项，优先保留创建时间较早、路径较浅的一份；尚未执行移除。");
    }

    /// <summary>
    /// 二次确认后移除选中的重复文件：按所选方式移入回收站或永久删除，完成后刷新当前结果。
    /// 扫描器会在移除前重新校验文件内容。
    /// </summary>
    private async void Remove(object? sender, RoutedEventArgs e)
    {
        if (_busy || _scan == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var selected = Results.SelectedItems.OfType<ScannedFile>().ToList();
        if (selected.Count == 0) { UiStatus.Set(Status, "请先选择文件。", UiStatusKind.Error); return; }
        var mode = RemovalMode.SelectedIndex == 0 ? FileRemovalMode.Recycle : FileRemovalMode.Permanent;
        var action = mode == FileRemovalMode.Recycle ? "移入 Windows 回收站" : "永久删除（不可恢复）";
        SetBusy(true);
        try
        {
            // 永久删除不可恢复，必须经用户确认。
            var listed = string.Join("\n", selected.Take(20).Select(f => f.FilePath));
            if (selected.Count > 20) listed += $"\n…等 {selected.Count} 项";
            var message = $"即将{action} {selected.Count} 个文件。\n每组必须保留至少一份；确认后仍会重新校验内容。\n\n" + listed;
            if (!await ConfirmationWindow.AskAsync(owner, message, "确认移除文件", "确认移除",
                mode == FileRemovalMode.Permanent)) { UiStatus.Set(Status, "已取消移除，没有改动文件。"); return; }
            UiStatus.Set(Status, $"正在复查并{action}…", UiStatusKind.Busy);
            Progress.IsVisible = true;
            var result = await _scanner.RemoveAsync(_scan, selected, mode);
            var removed = result.Outcomes.Where(o => o.Success).Select(o => o.Path).ToList();
            _scan = _scan.Without(removed);
            ApplyFilter();
            var failed = result.Outcomes.Count - removed.Count;
            UiStatus.Set(Status, $"已移除 {removed.Count}，未移除 {failed}。" + (result.Cancelled ? "用户取消了后续处理。" : ""),
                failed == 0 ? UiStatusKind.Success : UiStatusKind.Error);
            Warnings.Text = string.Join("\n", result.Outcomes.Where(o => !o.Success).Select(o => $"{o.Path}：{o.Error}"));
        }
        catch (Exception ex) { UiStatus.Set(Status, $"未完成移除：{ex.Message}", UiStatusKind.Error); }
        finally { Progress.IsVisible = false; SetBusy(false); }
    }

    /// <summary>加载最近的扫描记录供查看。</summary>
    private async void LoadHistory(object? sender, RoutedEventArgs e)
    {
        if (_busy || _history == null) return;
        HistoryButton.IsEnabled = false;
        try { History.ItemsSource = await _history.RecentAsync(); }
        catch (Exception ex) { UiStatus.Set(Status, $"加载记录失败：{ex.Message}", UiStatusKind.Error); }
        finally { HistoryButton.IsEnabled = !_busy; }
    }
}
