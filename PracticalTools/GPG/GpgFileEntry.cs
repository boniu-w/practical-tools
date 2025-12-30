using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using PracticalTools.FileManager.Model;

namespace PracticalTools.GPG;

public class GpgFileEntry : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    [ColumnHeader("文件名")] public string FileName { get; set; } = "";
    [ColumnHeader("文件路径")] public string? FilePath { get; set; }
    [ColumnHeader("文件创建时间")] public DateTime? FileCreateTime { get; set; }
    [ColumnHeader("文件大小[K]")] public double? FileSize { get; set; }
    [ColumnHeader("最后修改时间")] public DateTime? FileLastedModifiedTime { get; set; }
    [ColumnHeader("Hash值[MD5]")] public string? Md5Value { get; set; }

    public DateTime? CreateTime { get; set; }

    public DateTime? UpdateTime { get; set; }

    [ColumnHeader("类别")] public string? Type { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _isSelected;

    [NotMapped]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }
}