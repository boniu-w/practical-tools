using System.Windows;
using PracticalTools.GPG;
using PracticalTools.Sqlite;

namespace PracticalTools.Views;

public partial class GpgEditWindow : Window
{
    private readonly GpgFileEntryWrapper _wrapper;
    private readonly MyDbContext _dbContext;

    public GpgEditWindow(GpgFileEntryWrapper wrapper, MyDbContext dbContext)
    {
        InitializeComponent();
        _wrapper = wrapper;
        _dbContext = dbContext;
        DataContext = wrapper;

        // 只预填「说明」（身份/别名/密码通过绑定只读展示）
        TxtDescription.Text = wrapper.GpgFile.Description ?? "";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _wrapper.GpgFile.Description = TxtDescription.Text.Trim();

            var service = new GpgFileService(_dbContext);
            service.Update(_wrapper.GpgFile);

            MessageBox.Show("保存成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}