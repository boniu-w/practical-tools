using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PracticalTools.Views;

public partial class MoveFilePage : UserControl
{
    public MoveFilePage()
    {
        InitializeComponent();
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择要整理文件的文件夹" };
        if (dialog.ShowDialog() == true)
        {
            FolderPathTextBox.Text = dialog.FolderName;
        }
    }

    private void MoveFiles(object sender, RoutedEventArgs e)
    {
        string folderPath = FolderPathTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            MessageBox.Show("请先选择一个文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string suffixesText = SuffixTextBox.Text?.Trim() ?? "";
        string[] suffixes = suffixesText
            .Split(new[] { ',', '，', ';', '；', ' ', '　' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('.'))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (suffixes.Length == 0)
        {
            MessageBox.Show("请输入要移动的文件后缀", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var resultLines = new List<string>();
        int total = 0;
        foreach (string suffix in suffixes)
        {
            int count = Move(folderPath, suffix);
            total += count;
            resultLines.Add($"{suffix}: 移动了 {count} 个文件");
        }

        OutputText.Text = $"处理完成，共移动 {total} 个文件\n" + string.Join("\n", resultLines);
    }

    /// <summary>
    /// 把当前目录下指定后缀的文件移动到同名子目录（如 nef -> nef 目录）。
    /// </summary>
    private static int Move(string folderPath, string suffix)
    {
        DirectoryInfo currentDir = new DirectoryInfo(folderPath);
        DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(folderPath, suffix));

        // 目标目录不存在就创建
        if (!targetDir.Exists)
        {
            targetDir.Create();
        }

        int count = 0;
        // 先取快照，避免边遍历边移动导致的枚举问题
        foreach (FileInfo file in currentDir.EnumerateFiles().ToArray())
        {
            // 只处理普通文件 && 后缀匹配（不区分大小写）
            if (file.Extension.Equals("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                string target = Path.Combine(targetDir.FullName, file.Name);
                // 已存在则覆盖
                File.Move(file.FullName, target, true);
                count++;
            }
        }

        return count;
    }
}