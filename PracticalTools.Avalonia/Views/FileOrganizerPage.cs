using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.FileManager;

namespace PracticalTools.Desktop.Views;

public sealed partial class FileOrganizerPage : UserControl
{
    private readonly FileOrganizer _service = new();
    private OrganizePlan? _plan;
    private CancellationTokenSource? _cancel;

    public FileOrganizerPage()
    {
        InitializeComponent();
        FolderPath.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Invalidate(); };
        Suffixes.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Invalidate(); };
        DetachedFromVisualTree += (_, _) => _cancel?.Cancel();
    }

    private async void PickFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new()
            { Title = "选择整理目录", AllowMultiple = false });
            if (folders.Count == 0) return;
            using var folder = folders[0];
            FolderPath.Text = folder.TryGetLocalPath() ?? throw new IOException("请选择本地目录。");
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void PreviewPlan(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        Invalidate();
        var plan = await _service.PreviewAsync(FolderPath.Text ?? "", Suffixes.Text ?? "", _cancel!.Token);
        _plan = plan;
        MovePreview.ItemsSource = plan.Items;
        Apply.IsEnabled = plan.Items.Count > 0;
        if (plan.Items.Count == 0)
            ShowEmpty("没有匹配的文件", "检查目录和后缀后重新预览。");
        else
            EmptyPlan.IsVisible = false;
        UiStatus.Set(StatusText, $"共 {plan.Items.Count} 项；确认后移入后缀同名子目录，目标已存在时跳过，不覆盖。");
    });

    private async void ApplyPlan(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var plan = _plan ?? throw new InvalidOperationException("请先预览。");
        if (plan.Items.Count == 0) return;
        var listed = string.Join("\n", plan.Items.Take(20).Select(i => $"{i.Source} → {i.Target}"));
        if (plan.Items.Count > 20) listed += $"\n…等 {plan.Items.Count} 项";
        if (TopLevel.GetTopLevel(this) is not Window owner || !await ConfirmationWindow.AskAsync(owner,
            "确认移动下列文件？已存在的目标不会覆盖。\n" + listed, "确认整理", "确认移动")) return;
        var result = await _service.ApplyAsync(plan, _cancel!.Token);
        _plan = null;
        MovePreview.ItemsSource = null;
        Apply.IsEnabled = false;
        ShowEmpty("整理已完成", "修改输入后可以重新预览。");
        UiStatus.Set(StatusText, $"已移动 {result.Moved} 项，失败 {result.Errors.Count} 项。" +
            (result.Cancelled ? "已取消；完成的移动保留。" : "") + "\n" + string.Join("\n", result.Errors.Take(20)),
            result.Errors.Count == 0 ? UiStatusKind.Success : UiStatusKind.Error);
    });

    private void CancelRun(object? sender, RoutedEventArgs e) => _cancel?.Cancel();

    private void Invalidate()
    {
        _plan = null;
        MovePreview.ItemsSource = null;
        Apply.IsEnabled = false;
        ShowEmpty("还没有预览结果", "选择目录和后缀后点击“预览移动”。");
    }

    private void ShowEmpty(string title, string hint)
    {
        EmptyPlanTitle.Text = title;
        EmptyPlanHint.Text = hint;
        EmptyPlan.IsVisible = true;
    }

    private async Task Run(Func<Task> action)
    {
        if (_cancel != null) return;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        InputsPanel.IsEnabled = Preview.IsEnabled = Apply.IsEnabled = false;
        Cancel.IsEnabled = true;
        try { await action(); }
        catch (OperationCanceledException) { UiStatus.Set(StatusText, "已取消。"); }
        catch (Exception ex) { UiStatus.Set(StatusText, ex.Message, UiStatusKind.Error); }
        finally
        {
            _cancel = null;
            InputsPanel.IsEnabled = Preview.IsEnabled = true;
            Cancel.IsEnabled = false;
            Apply.IsEnabled = _plan != null && _plan.Items.Count > 0;
        }
    }
}
