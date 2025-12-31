using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using PracticalTools.FileManager.Model;

namespace PracticalTools.GPG;

public class GpgFile : INotifyPropertyChanged
{
    public long Id { get; set; }

    [ColumnHeader("身份")] public string Identity { get; set; } = "";
    [ColumnHeader("密码")] public string Passphrase { get; set; } = "";
    [ColumnHeader("密钥长度")] public int KeySize { get; set; } = 0;
    [ColumnHeader("密钥对有效时长")] public long ValidDurationSeconds { get; set; }
    [ColumnHeader("私钥")] public string SecretKey { get; set; } = "";
    [ColumnHeader("公钥")] public string PublicKey { get; set; } = "";
    [ColumnHeader("私钥文件名")] public string FileNamePrivate { get; set; } = "";
    [ColumnHeader("公钥文件名")] public string FileNamePublic { get; set; } = "";
    [ColumnHeader("创建时间")] public DateTime? FileCreateTime { get; set; }
    [ColumnHeader("私钥Hash值[MD5]")] public string? Md5ValuePrivate { get; set; }
    [ColumnHeader("公钥Hash值[MD5]")] public string? Md5ValuePublic { get; set; }
    [ColumnHeader("私钥的签名")] public string? SignaturePrivate { get; set; }
    [ColumnHeader("公钥的签名")] public string? SignaturePublic { get; set; }
    [ColumnHeader("类别")] public string? Type { get; set; }
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }


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