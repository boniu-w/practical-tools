using System.Collections.ObjectModel;
using System.Windows;
using PracticalTools.Sqlite;
using PracticalTools.vo;

namespace PracticalTools;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel _mainViewModel;
    public MainWindow()
    {
        InitializeComponent();
        _mainViewModel = new MainViewModel();
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
        // demo.InsertTheFile(null);
        demo.InsertBlackList();
        //
        // demo.Select();
        // demo.Close();

        // demo.List();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _mainViewModel.Blacklists = new ObservableCollection<Blacklist.Model.Blacklist>(_mainViewModel._myDbContext.Blacklist.OrderByDescending(e => e.UpdateTime));
        BlackL.ItemsSource = _mainViewModel.Blacklists;
    }
}