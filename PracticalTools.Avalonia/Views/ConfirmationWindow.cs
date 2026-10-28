using Avalonia;
using Avalonia.Controls;

namespace PracticalTools.Desktop.Views;

public sealed class ConfirmationWindow : Window
{
    public ConfirmationWindow(string message)
    {
        Title = "确认删除";
        Width = 560; Height = 260;
        var yes = new Button { Name = "ConfirmButton", Content = "确认删除" };
        var cancel = new Button { Name = "CancelButton", Content = "取消" };
        yes.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { cancel, yes } }
            }
        };
    }
    public static Task<bool> AskAsync(Window owner, string message) => new ConfirmationWindow(message).ShowDialog<bool>(owner);
}
