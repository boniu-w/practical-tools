using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PracticalTools.GPG;
using PracticalTools.vo;

namespace PracticalTools.Views;

public partial class GpgFileDataGrid : UserControl
{
    public GpgFileDataGrid()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击「操作」按钮 — 弹出 ContextMenu
    /// </summary>
    private void OnOperationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// 从 MenuItem 反查所属 Button，再拿 Tag 中的 Wrapper
    /// </summary>
    private static GpgFileEntryWrapper? GetWrapperFromMenuItem(object sender)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is Button btn &&
            btn.Tag is GpgFileEntryWrapper wrapper)
        {
            return wrapper;
        }
        return null;
    }

    /// <summary>
    /// 查看 — 弹出详情窗口
    /// </summary>
    private void OnViewClick(object sender, RoutedEventArgs e)
    {
        var wrapper = GetWrapperFromMenuItem(sender);
        if (wrapper == null) return;

        var detailWindow = new GpgDetailWindow(wrapper)
        {
            Owner = Window.GetWindow(this)
        };
        detailWindow.ShowDialog();
    }

    /// <summary>
    /// 修改 — 弹出编辑窗口
    /// </summary>
    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        var wrapper = GetWrapperFromMenuItem(sender);
        if (wrapper == null) return;

        var vm = DataContext as MainViewModel;
        if (vm?._myDbContext == null) return;

        var editWindow = new GpgEditWindow(wrapper, vm._myDbContext)
        {
            Owner = Window.GetWindow(this)
        };
        editWindow.ShowDialog();

        // 刷新列表中该行的显示
        GpgDataGrid.Items.Refresh();
    }

    /// <summary>
    /// 删除 — 确认后从数据库和列表中移除
    /// </summary>
    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var wrapper = GetWrapperFromMenuItem(sender);
        if (wrapper?.GpgFile == null) return;

        var result = MessageBox.Show(
            $"确定要删除密钥 \"{wrapper.GpgFile.IdentityName}\" 吗？\n此操作不可撤销。",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var vm = DataContext as MainViewModel;
            if (vm?._myDbContext == null) return;

            var service = new GpgFileService(vm._myDbContext);
            int deleted = service.Delete(wrapper.GpgFile.Id);
            if (deleted > 0)
            {
                vm.GpgFileWrapperList?.Remove(wrapper);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 单元格编辑结束 — 自动保存「说明」列到数据库
    /// </summary>
    private void GpgDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Column.Header?.ToString() == "说明")
        {
            var vm = DataContext as MainViewModel;
            if (vm?._myDbContext == null) return;

            if (e.Row.Item is GpgFileEntryWrapper wrapper)
            {
                try
                {
                    if (e.EditingElement is TextBox textBox)
                    {
                        wrapper.GpgFile.Description = textBox.Text;
                        var service = new GpgFileService(vm._myDbContext);
                        service.Update(wrapper.GpgFile);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}