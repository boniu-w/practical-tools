using Avalonia.Controls;
using Avalonia.Interactivity;
using PracticalTools.Activation.Service;
using PracticalTools.Storage;
using Serilog;

namespace PracticalTools.Desktop.Views;

public partial class ActivationWindow : Window
{
    private readonly ActivationService? _service;
    private bool _closed;
    public event Action? ActivationSucceeded;

    public ActivationWindow()
    {
        InitializeComponent();
        ActivateButton.IsEnabled = false;
        Status.Text = "请从应用入口启动。";
    }

    public ActivationWindow(ActivationService service, AppDataPaths paths) : this()
    {
        ActivateButton.IsEnabled = true;
        Status.Text = null;
        _service = service;
        DataDirectory.Text = $"数据目录：{paths.Root}";
        Closed += (_, _) => _closed = true;
    }

    private async void Activate(object? sender, RoutedEventArgs e)
    {
        if (_service == null) return;
        var code = Code.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code)) { Status.Text = "请输入激活码。"; return; }
        ActivateButton.IsEnabled = false;
        Code.IsEnabled = false;
        try
        {
            Status.Text = "正在验证…";
            var activated = await Task.Run(() => _service.Activate(code));
            if (_closed) return;
            if (!activated) { Status.Text = "激活码无效，请检查后重试。"; return; }
            Log.Information("软件激活成功");
            ActivationSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "激活失败");
            if (!_closed) Status.Text = $"激活失败：{ex.Message}";
        }
        finally { ActivateButton.IsEnabled = true; Code.IsEnabled = true; }
    }
}
