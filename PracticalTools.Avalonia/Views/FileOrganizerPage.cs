using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PracticalTools.FileManager;

namespace PracticalTools.Desktop.Views;

public sealed class FileOrganizerPage : UserControl
{
    private readonly FileOrganizer _service = new();
    private readonly TextBox _path = new() { Name = "FolderPath", Watermark = "整理目录（仅直接子文件）" };
    private readonly TextBox _suffixes = new() { Name = "Suffixes", Watermark = "后缀，例如 jpg,png,nef" };
    private readonly DataGrid _table = new() { Name = "MovePreview", AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _inputs = new() { Spacing = 8 };
    private OrganizePlan? _plan;
    private CancellationTokenSource? _cancel;
    public FileOrganizerPage()
    {
        var grid = new Grid { Margin = new Avalonia.Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button Add(string text, string name, Func<Task> action) { var b = new Button { Content = text, Name = name }; b.Click += async (_, _) => await Run(action); buttons.Children.Add(b); return b; }
        Add("选择目录", "PickFolder", async () =>
        {
            var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new() { AllowMultiple = false });
            if (folders.Count == 0) return; using var folder = folders[0]; _path.Text = folder.TryGetLocalPath();
        });
        Add("预览移动", "Preview", async () =>
        {
            _plan = null; _table.ItemsSource = null;
            _plan = await _service.PreviewAsync(_path.Text ?? "", _suffixes.Text ?? "", _cancel!.Token);
            _table.ItemsSource = _plan.Items; _status.Text = $"共 {_plan.Items.Count} 项；确认后移至后缀同名子目录，目标已存在时跳过，不覆盖。";
        });
        Add("确认整理", "Apply", async () =>
        {
            var plan = _plan ?? throw new InvalidOperationException("请先预览。");
            if (plan.Items.Count == 0) return;
            if (TopLevel.GetTopLevel(this) is not Window owner || !await ConfirmationWindow.AskAsync(owner,
                "确认移动下列文件？已存在的目标不会覆盖。\n" + string.Join("\n", plan.Items.Select(i => $"{i.Source} → {i.Target}")))) return;
            var result = await _service.ApplyAsync(plan, _cancel!.Token); _plan = null; _table.ItemsSource = null;
            _status.Text = $"已移动 {result.Moved} 项，失败 {result.Errors.Count} 项。" + (result.Cancelled ? "已取消；完成的移动保留。" : "") + "\n" + string.Join("\n", result.Errors.Take(20));
        });
        var cancel = new Button { Content = "取消", Name = "Cancel" }; cancel.Click += (_, _) => _cancel?.Cancel();
        _inputs.Children.Add(new TextBlock { Text = "按后缀整理文件：仅当前目录，不覆盖、不递归；先预览后确认。" });
        _inputs.Children.Add(_path); _inputs.Children.Add(_suffixes); _inputs.Children.Add(buttons);
        var header = new StackPanel { Spacing = 8 }; header.Children.Add(_inputs); header.Children.Add(cancel);
        grid.Children.Add(header); Grid.SetRow(_table, 1); grid.Children.Add(_table); Grid.SetRow(_status, 2); grid.Children.Add(_status); Content = grid;
        foreach (var name in new[] { "Source", "Target" }) _table.Columns.Add(new DataGridTextColumn { Header = name == "Source" ? "源文件" : "目标文件", Binding = new Binding(name), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _path.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Invalidate(); };
        _suffixes.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Invalidate(); };
        DetachedFromVisualTree += (_, _) => _cancel?.Cancel();
    }
    private void Invalidate() { _plan = null; _table.ItemsSource = null; }
    private async Task Run(Func<Task> action)
    {
        if (_cancel != null) return; using var cancel = new CancellationTokenSource(); _cancel = cancel; _inputs.IsEnabled = false;
        try { await action(); } catch (OperationCanceledException) { _status.Text = "已取消。"; } catch (Exception ex) { _status.Text = ex.Message; }
        finally { _cancel = null; _inputs.IsEnabled = true; }
    }
}
