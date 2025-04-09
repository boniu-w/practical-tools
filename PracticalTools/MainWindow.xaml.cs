using System.Windows;
using PracticalTools.Sqlite;

namespace PracticalTools;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
    }
    

    /*****************************************************************************************************************************/
    
    public void SqliteTest(Object sender, EventArgs args)
    {
        SQLiteDemo demo = new SQLiteDemo();
        // demo.Open();
        // demo.CreateTableBlackList();
        demo.InsertTheFile(null);
        // demo.InsertBlackList();
        //
        // demo.Select();
        // demo.Close();

        // demo.List();
    }
}