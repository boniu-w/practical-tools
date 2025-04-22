using System.ComponentModel.DataAnnotations;

namespace PracticalTools.Blacklist.Model;

public class BlacklistEntry
{
    /**
     * id
     */
    public long Id { get; set; }

    /**
     * 平台名称(淘宝, 知乎, b站)
     */
    [StringLength(255, MinimumLength = 1)]
    public string? PlatformName { get; set; }

    /**
     * 作者
     */
    [StringLength(255, MinimumLength = 1)]
    public string? Author { get; set; }

    /**
     * 作品
     */
    [StringLength(255, MinimumLength = 1)]
    public string? Product { get; set; }

    /**
     * 作品日期
     */
    public DateTime WorksTime { get; set; }

    /**
     * 原因
     */
    [StringLength(255, MinimumLength = 1)]
    public string? Reason { get; set; }

    /**
     * 类型(电商, 视频网站, 知识网站)  (允许空值但限制非空时长度)
     */
    [StringLength(255, MinimumLength = 1)]
    public string? WorksType { get; set; }

    /**
     * 备注  (限制最大长度为 255 字符)
     */
    [MaxLength(255)]
    public string? Remarks { get; set; }

    public DateTime? CreateTime { get; set; }

    public DateTime? UpdateTime { get; set; }

    [StringLength(255, MinimumLength = 1)]
    public string? CreateUser { get; set; }

    [StringLength(255, MinimumLength = 1)]
    public string? UpdateUser { get; set; }

    /*
     * 统一社会信用代码
     */
    [StringLength(255, MinimumLength = 1)]
    public string? UnifiedSocialCreditIdentifier { get; set; }
}