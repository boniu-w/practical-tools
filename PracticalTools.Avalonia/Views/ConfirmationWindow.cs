using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PracticalTools.Desktop.Views;

public sealed class ConfirmationWindow : Window
{
    public ConfirmationWindow(string message, string title, string confirmText, bool destructive)
    {
        Title = title;
        Width = 560;
        Height = 340;
        MinWidth = 460;
        MinHeight = 280;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock { Text = title };
        heading.Classes.Add("pageTitle");
        var body = new TextBlock { Text = message };
        body.Classes.Add("dialogText");
        var messageView = new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(messageView, 1);

        var yes = new Button { Name = "ConfirmButton", Content = confirmText };
        yes.Classes.Add(destructive ? "danger" : "primary");
        yes.Click += (_, _) => Close(true);
        var cancel = new Button { Name = "CancelButton", Content = "取消" };
        cancel.Click += (_, _) => Close(false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, yes }
        };
        Grid.SetRow(buttons, 2);

        Content = new Grid
        {
            Margin = new Thickness(28),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 18,
            Children = { heading, messageView, buttons }
        };
    }

    /// <summary>显示确认对话框；<paramref name="destructive"/> 为 true 时确认按钮使用危险样式。</summary>
    public static Task<bool> AskAsync(Window owner, string message,
        string title = "请确认", string confirmText = "确认", bool destructive = false) =>
        new ConfirmationWindow(message, title, confirmText, destructive).ShowDialog<bool>(owner);
}
