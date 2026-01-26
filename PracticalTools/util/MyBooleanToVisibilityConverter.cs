using System.Windows;
using MaterialDesignThemes.Wpf.Converters;

namespace PracticalTools.util;

public class MyBooleanToVisibilityConverter : BooleanConverter<Visibility>
{
    public static readonly MyBooleanToVisibilityConverter CollapsedInstance = new()
        { FalseValue = Visibility.Collapsed, TrueValue = Visibility.Visible };

    public static readonly MyBooleanToVisibilityConverter NotCollapsedInstance = new()
        { FalseValue = Visibility.Visible, TrueValue = Visibility.Collapsed };

    public MyBooleanToVisibilityConverter() : base(Visibility.Visible, Visibility.Collapsed)
    {
    }
}