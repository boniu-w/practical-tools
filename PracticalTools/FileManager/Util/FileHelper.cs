using System.Data;
using System.IO;
using Microsoft.Win32;
using OfficeOpenXml;

namespace PracticalTools.FileManager.Util;

public class FileHelper
{
    
    static FileHelper()
    {
        // 设置许可证上下文为非商业用途
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    // 导入文件（读取文件内容）
    public static string ImportFile(string filter = "All Files (*.*)|*.*")
    {
        try
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = filter // 设置文件类型过滤器
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // 获取选中的文件路径
                string filePath = openFileDialog.FileName;

                // 读取文件内容并返回
                return File.ReadAllText(filePath);
            }
        }
        catch (Exception ex)
        {
            // 异常处理
            Console.WriteLine($"导入文件时发生错误: {ex.Message}");
        }

        return null; // 如果没有选择文件或者发生错误，返回 null
    }

    // 导出文件（保存文件）
    public static bool ExportFile(string content, string defaultFileName = "output.txt",
        string filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*")
    {
        try
        {
            // 创建保存文件对话框
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                FileName = defaultFileName, // 默认文件名
                Filter = filter // 设置文件类型过滤器
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                // 获取保存文件的路径
                string filePath = saveFileDialog.FileName;

                // 写入内容到文件
                File.WriteAllText(filePath, content);

                return true; // 导出成功
            }
        }
        catch (Exception ex)
        {
            // 异常处理
            Console.WriteLine($"导出文件时发生错误: {ex.Message}");
        }

        return false; // 如果没有保存文件或发生错误，返回 false
    }

    // 导入 Excel 文件（读取内容）
    public static DataTable ImportExcelFile(string filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*")
    {
        try
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = filter // 设置文件类型过滤器
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // 获取选中的文件路径
                string filePath = openFileDialog.FileName;

                // 读取 Excel 文件
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0]; // 获取第一个工作表
                    var table = new DataTable();

                    // 获取工作表的列头
                    for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                    {
                        table.Columns.Add(worksheet.Cells[1, col].Text);
                    }

                    // 获取工作表的数据
                    for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                    {
                        var newRow = table.NewRow();
                        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                        {
                            newRow[col - 1] = worksheet.Cells[row, col].Text;
                        }

                        table.Rows.Add(newRow);
                    }

                    return table; // 返回数据表
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"导入 Excel 文件时发生错误: {ex.Message}");
        }

        return null; // 如果没有选择文件或发生错误，返回 null
    }

    // 导出数据到 Excel 文件
    public static bool ExportToExcel(DataTable data, string defaultFileName = "output.xlsx",
        string filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*")
    {
        try
        {
            // 创建保存文件对话框
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                FileName = defaultFileName, // 默认文件名
                Filter = filter // 设置文件类型过滤器
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                // 获取保存文件的路径
                string filePath = saveFileDialog.FileName;

                // 创建 Excel 文件
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Sheet1"); // 创建一个新的工作表

                    // 添加列头
                    for (int col = 0; col < data.Columns.Count; col++)
                    {
                        worksheet.Cells[1, col + 1].Value = data.Columns[col].ColumnName;
                    }

                    // 添加数据
                    for (int row = 0; row < data.Rows.Count; row++)
                    {
                        for (int col = 0; col < data.Columns.Count; col++)
                        {
                            worksheet.Cells[row + 2, col + 1].Value = data.Rows[row][col];
                        }
                    }

                    // 保存 Excel 文件
                    FileInfo fi = new FileInfo(filePath);
                    package.SaveAs(fi);

                    return true; // 导出成功
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"导出 Excel 文件时发生错误: {ex.Message}");
        }

        return false; // 如果没有保存文件或发生错误，返回 false
    }
}