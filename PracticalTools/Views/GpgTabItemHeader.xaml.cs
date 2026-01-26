using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PracticalTools.Views;

public partial class GpgTabItemHeader : UserControl
{
    public GpgTabItemHeader()
    {
        InitializeComponent();
    }

    private CancellationTokenSource _cts;

    private async void TabHeader_OnMouseEnter(object sender, MouseEventArgs e)
    {
        // 取消上一次未完成的操作（防止多次进入触发多个延迟）
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            await Task.Delay(3000, _cts.Token); // 等待3秒，可取消
            EllipseUnreadTip.Fill = new SolidColorBrush(Colors.ForestGreen);
        }
        catch (OperationCanceledException)
        {
            // 被取消是正常行为，无需处理
        }
    }

    private void TabHeader_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _cts?.Cancel(); // 取消等待中的变色
        // EllipseUnreadTip.Fill = new SolidColorBrush(Colors.BlueViolet);
    }
}