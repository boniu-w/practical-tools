using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.GPG;

namespace PracticalTools.Desktop.Views;

public partial class KeyLibraryPage : UserControl
{
    private KeyLibrary? _library;
    private Task? _reloadTask;
    public KeyLibraryPage()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) => await ReloadAsync();
    }
    public void Configure(KeyLibrary library) => _library = library;

    public Task ReloadAsync() => _reloadTask is { IsCompleted: false } ? _reloadTask : _reloadTask = ReloadCoreAsync();

    private async Task ReloadCoreAsync()
    {
        if (_library == null) return;
        try
        {
            var items = await _library.ListAsync();
            Keys.ItemsSource = items;
            EmptyKeys.IsVisible = items.Count == 0;
        }
        catch (Exception ex) { UiStatus.Set(Status, $"加载失败：{ex.Message}", UiStatusKind.Error); }
    }

    private KeySummary SelectedKey => Keys.SelectedItem as KeySummary
        ?? throw new InvalidOperationException("请先选择一条密钥。");

    private void Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (Keys.SelectedItem is not KeySummary key)
        {
            Details.Text = "选择密钥查看详情。";
            KeyName.Text = Description.Text = "";
            return;
        }
        KeyName.Text = key.Name;
        Description.Text = key.Description;
        Details.Text = $"指纹：{key.Fingerprint}\n类型：{key.Kind} · 密钥位数 {key.KeySize}\n生效（UTC）：{key.BeginsOn} · 到期（UTC）：{key.EndsOn?.ToString() ?? "永不过期"}";
    }

    private async void Refresh(object? sender, RoutedEventArgs e) => await ReloadAsync();

    private async void Import(object? sender, RoutedEventArgs e)
    {
        if (_library == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        await new KeyImportWindow(_library).ShowDialog<bool>(owner);
        await ReloadAsync();
    }

    private async void SaveMetadata(object? sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        try
        {
            var id = SelectedKey.Id;
            Form.IsEnabled = false;
            var updated = await _library.UpdateMetadataAsync(id, KeyName.Text ?? "", Description.Text ?? "");
            UiStatus.Set(Status, updated ? "已保存。" : "密钥已不存在，请刷新。",
                updated ? UiStatusKind.Success : UiStatusKind.Error);
            await ReloadAsync();
        }
        catch (Exception ex) { UiStatus.Set(Status, ex.Message, UiStatusKind.Error); }
        finally { Form.IsEnabled = true; }
    }

    private async void Delete(object? sender, RoutedEventArgs e)
    {
        if (_library == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            var selected = SelectedKey;
            if (!await ConfirmationWindow.AskAsync(owner, $"删除“{selected.Name}”？\n指纹：{selected.Fingerprint}\n此操作将移除库中的密钥和口令，请确认已有备份。",
                "删除密钥", "删除密钥", destructive: true))
            { UiStatus.Set(Status, "已取消删除。"); return; }
            Form.IsEnabled = false;
            UiStatus.Set(Status, await _library.DeleteAsync(selected.Id) ? "已删除。" : "密钥已不存在。",
                UiStatusKind.Success);
            await ReloadAsync();
        }
        catch (Exception ex) { UiStatus.Set(Status, ex.Message, UiStatusKind.Error); }
        finally { Form.IsEnabled = true; }
    }

    private async void ExportPublic(object? sender, RoutedEventArgs e) => await ExportAsync(false);
    private async void ExportSecret(object? sender, RoutedEventArgs e) => await ExportAsync(true);
    private async Task ExportAsync(bool secret)
    {
        if (_library == null) return;
        try
        {
            var selected = SelectedKey;
            if (secret && !selected.HasSecret) throw new InvalidOperationException("该记录没有私钥。");
            using var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "导出密钥（请选择新文件名）",
                SuggestedFileName = $"{selected.Fingerprint}-{(secret ? "secret" : "public")}.asc"
            });
            if (file == null) return;
            var path = file.TryGetLocalPath() ?? throw new IOException("请选择本地保存位置。");
            await _library.ExportAsync(selected.Id, path, secret);
            UiStatus.Set(Status, $"已导出：{path}", UiStatusKind.Success);
        }
        catch (Exception ex) { UiStatus.Set(Status, $"导出失败：{ex.Message}", UiStatusKind.Error); }
    }
}
