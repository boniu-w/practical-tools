using System.Windows;
using PracticalTools.GPG;

namespace PracticalTools.Views;

public partial class GpgDetailWindow : Window
{
    public GpgDetailWindow(GpgFileEntryWrapper wrapper)
    {
        InitializeComponent();
        DataContext = wrapper;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}