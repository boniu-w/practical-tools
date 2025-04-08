using System.ComponentModel;
using System.Windows;
using PracticalTools.Sqlite;

namespace PracticalTools;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Dispose()
    {
        throw new NotImplementedException();
    }
    public MainWindow()
    {
        InitializeComponent();
    }
    
    public void SqliteTest(Object sender, EventArgs args)
    {
        SQLiteDemo demo = new SQLiteDemo();
        // demo.Open();
        // demo.CreateTable();
        // demo.Insert(null);
        //
        // demo.Select();
        // demo.Close();
            
        demo.List();
    }

    
}