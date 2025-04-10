using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Sqlite;
using PracticalTools.vo;

namespace PracticalTools;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // public MainWindow()
    // {
    //     var viewModel = ((App)Application.Current)._serviceProvider.GetRequiredService<MainViewModel>();
    //     DataContext = viewModel;
    //     InitializeComponent();
    // }
    
    public MainWindow(MainViewModel mainViewModel, SQLiteDemo sqLiteDemo)
    {
        InitializeComponent();
        DataContext = mainViewModel;
        _sqLiteDemo = sqLiteDemo;
    }

    private SQLiteDemo _sqLiteDemo;
    
    /*****************************************************************************************************************************/

    public void SqliteTest(Object sender, EventArgs args)
    {
        // SQLiteDemo demo = new SQLiteDemo();
        // demo.Open();
        // demo.CreateTableBlackList();
        // demo.InsertTheFile(null);
        // demo.InsertBlackList();
        //
        // demo.Select();
        // demo.Close();

        // demo.List();
        
        _sqLiteDemo.InsertBlackList();
    }
}