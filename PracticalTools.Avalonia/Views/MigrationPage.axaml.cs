using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PracticalTools.Migration;

namespace PracticalTools.Desktop.Views;

public partial class MigrationPage : UserControl
{
    private MigrationPackageService? _service;
    private MigrationPreview? _preview;
    public MigrationPage()
    {
        InitializeComponent();
        ChooseLegacyButton.IsEnabled = ExportLegacyButton.IsEnabled = OperatingSystem.IsWindows();
        // TextChanged is dispatched later; invalidate synchronously before another button can be invoked.
        Password.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) InvalidatePreview(); };
        PackagePath.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) InvalidatePreview(); };
    }
    public void Configure(MigrationPackageService service) => _service = service;

    private void InvalidatePreview()
    {
        _preview = null;
        if (ConfirmImportButton != null) ConfirmImportButton.IsEnabled = false;
        if (PreviewText != null) PreviewText.Text = "";
    }

    private async Task<string?> PickAsync(string title)
    {
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new() { Title = title, AllowMultiple = false });
        if (files.Count == 0) return null;
        using var file = files[0];
        return file.TryGetLocalPath() ?? throw new IOException("请选择本地文件。");
    }

    private async void ChooseLegacy(object? sender, RoutedEventArgs e)
    {
        try { var path = await PickAsync("选择原 Windows 实用工具数据库"); if (path != null) LegacyPath.Text = path; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void ChoosePackage(object? sender, RoutedEventArgs e)
    {
        try { var path = await PickAsync("选择加密迁移包"); if (path != null) PackagePath.Text = path; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async Task BusyAsync(Func<Task> action)
    {
        if (_service == null) { Status.Text = "迁移服务尚未初始化。"; return; }
        Form.IsEnabled = false;
        Status.Text = "处理中…";
        try { await action(); }
        catch (Exception ex) { InvalidatePreview(); Status.Text = $"未完成：{ex.Message}"; }
        finally { Form.IsEnabled = true; }
    }

    private async void ExportLegacy(object? sender, RoutedEventArgs e) => await ExportAsync(true);
    private async void ExportCurrent(object? sender, RoutedEventArgs e) => await ExportAsync(false);
    private async Task ExportAsync(bool legacy)
    {
        await BusyAsync(async () =>
        {
            var password = Password.Text ?? "";
            if (password.Length < 12 || string.IsNullOrWhiteSpace(password)) throw new ArgumentException("迁移口令至少需要 12 个字符。");
            if (password != Confirmation.Text) throw new ArgumentException("两次迁移口令不一致。");
            var source = LegacyPath.Text ?? "";
            if (legacy && !File.Exists(source)) throw new ArgumentException("请先选择存在的旧数据库。");
            using var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "保存加密迁移包（请选择新文件名）", SuggestedFileName = "PracticalTools.ptmigration"
            });
            if (file == null) { Status.Text = "已取消导出。"; return; }
            var target = file.TryGetLocalPath() ?? throw new IOException("请选择本地保存位置。");
            if (legacy) await _service!.ExportLegacyAsync(source, target, password);
            else await _service!.ExportCurrentAsync(target, password);
            Status.Text = $"迁移包已保存：{target}。请在目标电脑输入同一迁移口令预览后导入。";
        });
    }

    private async void Preview(object? sender, RoutedEventArgs e)
    {
        _preview = null;
        ConfirmImportButton.IsEnabled = false;
        await BusyAsync(async () =>
        {
            _preview = await _service!.PreviewAsync(PackagePath.Text ?? "", Password.Text ?? "");
            PreviewText.Text = $"密钥 {_preview.Keys} · 黑名单 {_preview.Blacklist} · 扫描记录 {_preview.Scans} · 文件记录 {_preview.Files}\n" +
                "密钥指纹（最多展示 10 个）：\n" + string.Join("\n", _preview.Fingerprints.Take(10));
            ConfirmImportButton.IsEnabled = true;
            Status.Text = "验证成功，尚未写入数据。确认后导入；重复项跳过，激活状态保持当前记录。";
        });
    }

    private async void Import(object? sender, RoutedEventArgs e)
    {
        if (_preview == null) return;
        var digest = _preview.Digest;
        await BusyAsync(async () =>
        {
            var result = await _service!.ImportAsync(PackagePath.Text ?? "", Password.Text ?? "", digest);
            _preview = null;
            ConfirmImportButton.IsEnabled = false;
            Status.Text = result.ToString();
        });
    }
}
