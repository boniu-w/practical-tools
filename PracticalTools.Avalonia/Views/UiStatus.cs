using Avalonia.Controls;

namespace PracticalTools.Desktop.Views;

internal enum UiStatusKind
{
    Neutral,
    Busy,
    Success,
    Error
}

/// <summary>统一设置状态文本及其语义颜色。</summary>
internal static class UiStatus
{
    /// <summary>更新状态文本，并切换忙/成功/错误样式。</summary>
    public static void Set(TextBlock status, string text, UiStatusKind kind = UiStatusKind.Neutral)
    {
        status.Text = text;
        status.Classes.Remove("busy");
        status.Classes.Remove("success");
        status.Classes.Remove("error");
        switch (kind)
        {
            case UiStatusKind.Busy: status.Classes.Add("busy"); break;
            case UiStatusKind.Success: status.Classes.Add("success"); break;
            case UiStatusKind.Error: status.Classes.Add("error"); break;
        }
    }
}
