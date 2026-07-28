using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Activation.Service;

namespace PracticalTools.Views;

public partial class ActivationWindow : Window
{
    private readonly ActivationService _activationService;

    public ActivationWindow()
    {
        InitializeComponent();

        // 从 DI 获取 ActivationService
        var scope = App.ServiceProvider.CreateScope();
        _activationService = scope.ServiceProvider.GetRequiredService<ActivationService>();
    }

    /// <summary>
    /// 点击激活按钮
    /// </summary>
    private void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        string code = ActivationCodeBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(code))
        {
            ShowError("请输入激活码");
            return;
        }

        if (_activationService.Activate(code))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ShowError("激活码无效，请检查后重试");
        }
    }

    /// <summary>
    /// 关闭按钮
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }
}
