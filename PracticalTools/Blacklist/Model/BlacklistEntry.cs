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
    public string? PlatformName { get; set; }
    
    /**
     * 作者
     */
    public string? Author { get; set; }
    
    /**
     * 作品
     */
    public string? Product { get; set; }
    
    /**
     * 作品日期
     */
    public DateTime WorksTime { get; set; }
    
    /**
     * 原因
     */
    public string? Reason { get; set; }
    
    /**
     * 类型(电商, 视频网站, 知识网站)
     */
    public string? WorksType { get; set; }
    
    /**
     * 备注
     */
    public string? Remarks { get; set; }

    public DateTime? CreateTime { get; set; }

    public DateTime? UpdateTime { get; set; }

    public string? CreateUser { get; set; }

    public string? UpdateUser { get; set; }
}