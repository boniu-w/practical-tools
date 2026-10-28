namespace PracticalTools.Activation.Model;

/// <summary>
/// 激活记录，数据库中只保留一条记录
/// </summary>
public class ActivationRecord
{
    public long Id { get; set; }
    public string ActivationCode { get; set; } = "";
    public DateTime? ActivateTime { get; set; }
    public DateTime? CreateTime { get; set; }
}
