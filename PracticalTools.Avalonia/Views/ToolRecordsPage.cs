using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PracticalTools.Blacklist.Model;
using PracticalTools.FileManager.Model;
using PracticalTools.Storage;

namespace PracticalTools.Desktop.Views;

public sealed class ToolRecordsPage : UserControl
{
    private readonly ToolRecords _store;
    private readonly bool _blacklist;
    private readonly DataGrid _table = new() { Name = "Records", IsReadOnly = true, AutoGenerateColumns = false, MinHeight = 150 };
    private readonly TextBox _search = new() { Watermark = "搜索名称、路径；黑名单按平台、作者或作品搜索" };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _form = new() { Spacing = 8 };
    private readonly StackPanel _emptyTable;
    private readonly TextBox[] _fields;
    private bool _busy;
    private long _editing;
    private Task? _reload;
    public ToolRecordsPage(ToolRecords store, bool blacklist)
    {
        _store = store; _blacklist = blacklist;
        var names = blacklist ? new[] { "平台", "作者", "作品", "作品日期 yyyy-MM-dd", "原因", "类型", "备注", "统一社会信用代码" } : new[] { "类别" };
        _fields = names.Select((n, i) => new TextBox { Name = "Field" + i, Watermark = n }).ToArray();
        foreach (var (header, property) in blacklist ? new[] { ("平台", "PlatformName"), ("作者", "Author"), ("作品", "Product"), ("原因", "Reason"), ("日期", "WorksTime") }
            : new[] { ("文件名", "FileName"), ("路径", "FilePath"), ("大小 KiB", "FileSize"), ("MD5", "Md5Value"), ("类别", "Type") })
            _table.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("刷新 / 搜索", "Refresh", ReloadAsync));
        actions.Children.Add(Button(blacklist ? "新建记录" : "添加文件", "Add", blacklist ? () => { NewRecord(); return Task.CompletedTask; } : AddFiles));
        actions.Children.Add(Button("保存", "Save", Save));
        actions.Children.Add(Button("删除记录", "Delete", Delete));
        var editor = new WrapPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < _fields.Length; i++)
        {
            var field = _fields[i];
            field.Width = 220;
            var label = new TextBlock { Text = names[i] };
            label.Classes.Add("fieldLabel");
            var box = new StackPanel { Spacing = 4, Children = { label, field }, Margin = new Avalonia.Thickness(0, 0, 12, 12) };
            editor.Children.Add(box);
        }

        var title = new TextBlock { Text = blacklist ? "黑名单" : "文件记录" };
        title.Classes.Add("pageTitle");
        var subtitle = new TextBlock
        {
            Text = blacklist ? "人工维护的黑名单记录，不参与文件删除或扫描过滤。" : "文件处理记录，删除记录不会删除磁盘文件。"
        };
        subtitle.Classes.Add("muted");
        var pageHeader = new StackPanel { Spacing = 6, Children = { title, subtitle } };

        _form.Children.Add(_search);
        _form.Children.Add(actions);
        var formCard = new Border { Child = _form };
        formCard.Classes.Add("card");
        var emptyTitle = new TextBlock { Text = blacklist ? "还没有黑名单记录" : "还没有文件记录" };
        emptyTitle.Classes.Add("emptyTitle");
        var emptyHint = new TextBlock { Text = blacklist ? "点击“新建记录”开始维护。" : "点击“添加文件”记录一个文件。" };
        emptyHint.Classes.Add("emptyHint");
        _emptyTable = new StackPanel
        {
            Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            Children = { emptyTitle, emptyHint }
        };
        var tableGrid = new Grid { Children = { _table, _emptyTable } };
        var tableCard = new Border { Child = tableGrid, Padding = new Avalonia.Thickness(0), ClipToBounds = true };
        tableCard.Classes.Add("card");
        _status.Classes.Add("status");
        var editorTitle = new TextBlock { Text = blacklist ? "新建 / 编辑记录" : "编辑类别" };
        editorTitle.Classes.Add("sectionTitle");
        var editorBox = new StackPanel { Spacing = 10, Children = { editorTitle, editor } };
        var editorCard = new Border { Child = editorBox };
        editorCard.Classes.Add("card");

        var layout = new Grid
        {
            Margin = new Avalonia.Thickness(32),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 16,
            Children = { pageHeader, formCard, tableCard, editorCard, _status }
        };
        Grid.SetRow(formCard, 1);
        Grid.SetRow(tableCard, 2);
        Grid.SetRow(editorCard, 3);
        Grid.SetRow(_status, 4);
        Content = layout;
        _table.SelectionChanged += (_, _) => Fill();
        AttachedToVisualTree += async (_, _) => await Execute(ReloadAsync);
        NewRecord();
    }
    private Button Button(string label, string name, Func<Task> action)
    {
        var b = new Button { Content = label, Name = name };
        if (name == "Save") b.Classes.Add("primary");
        else if (name == "Delete") b.Classes.Add("danger");
        b.Click += async (_, _) => await Execute(action);
        return b;
    }
    private async Task Execute(Func<Task> action)
    {
        if (_busy) return; _busy = true; _form.IsEnabled = _table.IsEnabled = false;
        try { await action(); } catch (Exception ex) { UiStatus.Set(_status, ex.Message, UiStatusKind.Error); }
        finally { _busy = false; _form.IsEnabled = _table.IsEnabled = true; }
    }
    public Task ReloadAsync() => _reload is { IsCompleted: false } ? _reload : _reload = ReloadCore();
    private async Task ReloadCore()
    {
        var query = _search.Text?.Trim() ?? "";
        if (_blacklist)
        {
            var items = await _store.BlacklistAsync(query);
            _table.ItemsSource = items;
            _emptyTable.IsVisible = items.Count == 0;
        }
        else
        {
            var items = await _store.FilesAsync(query);
            _table.ItemsSource = items;
            _emptyTable.IsVisible = items.Count == 0;
        }
        NewRecord();
        UiStatus.Set(_status, "最多显示 1000 条记录；可输入关键词缩小范围。");
    }
    private void NewRecord() { _editing = 0; _table.SelectedItem = null; foreach (var f in _fields) f.Text = ""; if (_blacklist) _fields[3].Text = DateTime.Today.ToString("yyyy-MM-dd"); }
    private void Fill()
    {
        if (_table.SelectedItem is MyFile f) _fields[0].Text = f.Type;
        if (_table.SelectedItem is not BlacklistEntry b) return;
        _editing = b.Id;
        string?[] values = [b.PlatformName, b.Author, b.Product, b.WorksTime.ToString("yyyy-MM-dd"), b.Reason, b.WorksType, b.Remarks, b.UnifiedSocialCreditIdentifier];
        for (int i = 0; i < values.Length; i++) _fields[i].Text = values[i];
    }
    private async Task AddFiles()
    {
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new() { AllowMultiple = true, Title = "添加文件记录" });
        try { if (files.Count > 0) await _store.AddFilesAsync(files.Select(f => f.TryGetLocalPath() ?? throw new IOException("请选择本地文件。"))); }
        finally { foreach (var file in files) file.Dispose(); }
        await ReloadAsync();
    }
    private async Task Save()
    {
        if (!_blacklist)
        {
            if (_table.SelectedItem is not MyFile f) throw new ArgumentException("请选择文件记录。");
            await _store.UpdateFileAsync(f.Id, _fields[0].Text ?? "");
        }
        else
        {
            string? Value(int i) => string.IsNullOrWhiteSpace(_fields[i].Text) ? null : _fields[i].Text!.Trim();
            if (!DateTime.TryParseExact(Value(3), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new ArgumentException("作品日期格式应为 yyyy-MM-dd。");
            await _store.SaveBlacklistAsync(new BlacklistEntry { Id = _editing, PlatformName = Value(0), Author = Value(1), Product = Value(2), WorksTime = date,
                Reason = Value(4), WorksType = Value(5), Remarks = Value(6), UnifiedSocialCreditIdentifier = Value(7) });
            NewRecord();
        }
        await ReloadAsync(); UiStatus.Set(_status, "已保存记录。", UiStatusKind.Success);
    }
    private async Task Delete()
    {
        var selected = _table.SelectedItem ?? throw new ArgumentException("请选择记录。");
        var description = selected is MyFile f ? f.FilePath : $"{((BlacklistEntry)selected).PlatformName} / {((BlacklistEntry)selected).Author}";
        if (TopLevel.GetTopLevel(this) is not Window owner || !await ConfirmationWindow.AskAsync(owner,
            $"删除这条数据库记录？\n{description}\n磁盘文件不会删除。", "删除记录", "删除记录", destructive: true)) return;
        if (selected is MyFile file) await _store.DeleteFileAsync(file.Id); else await _store.DeleteBlacklistAsync(((BlacklistEntry)selected).Id);
        NewRecord(); await ReloadAsync();
        UiStatus.Set(_status, "已删除记录；磁盘文件未受影响。", UiStatusKind.Success);
    }
}
