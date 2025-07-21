using System.Data;
using System.Globalization;
using System.Reflection;

namespace PracticalTools.FileManager.Model;

public class MyFile
{
    public string Id { get; set; }
    [ColumnHeader("文件名")] public string FileName { get; set; }
    [ColumnHeader("文件路径")] public string? FilePath { get; set; }
    [ColumnHeader("文件创建时间")] public DateTime? FileCreateTime { get; set; }
    [ColumnHeader("文件大小[K]")] public double? FileSize { get; set; }
    public DateTime? FileLastedModifiedTime { get; set; }

    [ColumnHeader("Hash值[MD5]")] public string? Md5Value { get; set; }

    public DateTime? CreateTime { get; set; }

    public DateTime? UpdateTime { get; set; }
    
    public string? Type { get; set; }

    public override string ToString()
    {
        return $"Id: {Id}\n" +
               $"FileName: {FileName}\n" +
               $"FilePath: {FilePath}\n" +
               $"FileCreateTime: {FileCreateTime}\n" +
               $"FileSize: {FileSize}\n" +
               $"FileLastedModifiedTime: {FileLastedModifiedTime}\n" +
               $"HashValue: {Md5Value}";
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class ColumnHeaderAttribute : Attribute
{
    public string Header { get; }

    public ColumnHeaderAttribute(string header)
    {
        Header = header;
    }
}

public static class DataTableMapper
{
    public static List<MyFile> MapToMyFileList(DataTable dt)
    {
        List<MyFile> fileList = new List<MyFile>();

        // 获取 MyFile 类的属性，并根据 ColumnHeader 特性查找列名
        var properties = typeof(MyFile).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(ColumnHeaderAttribute), false))
            .ToList();

        foreach (DataRow row in dt.Rows)
        {
            var myFile = new MyFile();

            foreach (var prop in properties)
            {
                var columnHeader = prop.GetCustomAttribute<ColumnHeaderAttribute>().Header;
                if (dt.Columns.Contains(columnHeader))
                {
                    var value = row[columnHeader];
                    if (value != DBNull.Value)
                    {
                        try
                        {
                            if (prop.PropertyType == typeof(DateTime?))
                            {
                                DateTime parsedDate;
                                if (value is string strValue)
                                {
                                    if (DateTime.TryParseExact(strValue, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None,
                                            out parsedDate))
                                    {
                                        prop.SetValue(myFile, parsedDate);
                                    }
                                    else if (DateTime.TryParseExact(strValue, "MM/dd/yy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                                                 out parsedDate))
                                    {
                                        prop.SetValue(myFile, parsedDate);
                                    }
                                    else if (DateTime.TryParseExact(strValue, "yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                                                 out parsedDate))
                                    {
                                        prop.SetValue(myFile, parsedDate);
                                    }
                                    else if (DateTime.TryParseExact(strValue, "yyyy/MM/d HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                                                 out parsedDate))
                                    {
                                        prop.SetValue(myFile, parsedDate);
                                    }
                                    else if (DateTime.TryParseExact(strValue, "yyyy/M/dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                                                 out parsedDate))
                                    {
                                        prop.SetValue(myFile, parsedDate);
                                    }
                                    else if (DateTime.TryParseExact(strValue, "yyyy/M/d HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                                                 out parsedDate))
                                    {
                                        prop.SetValue(myFile, parsedDate);
                                    }
                                    else
                                    {
                                        prop.SetValue(myFile, null); // 如果解析失败，则设置为 null
                                    }
                                }
                                else if (DateTime.TryParse(value.ToString(), out parsedDate))
                                {
                                    prop.SetValue(myFile, parsedDate);
                                }
                            }
                            // 检查并转换字符串为 double? 类型
                            else if (prop.PropertyType == typeof(double?) && value is string doubleValue)
                            {
                                double parsedValue;
                                if (double.TryParse(doubleValue, out parsedValue))
                                {
                                    prop.SetValue(myFile, parsedValue);
                                }
                                else
                                {
                                    prop.SetValue(myFile, null); // 如果解析失败，则设置为 null
                                }
                            }
                            else
                            {
                                // 对于其他类型，直接进行转换
                                prop.SetValue(myFile, Convert.ChangeType(value, prop.PropertyType));
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"转换失败: {ex.Message}");
                            // 设置为 null 或默认值
                            prop.SetValue(myFile, null);
                        }
                    }
                }
            }

            fileList.Add(myFile);
        }

        return fileList;
    }
}