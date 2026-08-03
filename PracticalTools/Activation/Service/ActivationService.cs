using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Activation.Model;
using PracticalTools.Sqlite;

namespace PracticalTools.Activation.Service;

/// <summary>
/// 简易固定激活码服务
/// </summary>
public class ActivationService
{
    /// <summary>
    /// 预设的有效激活码列表（不区分大小写）
    /// </summary>
    private static readonly HashSet<string> ValidActivationCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PT2024-9A7F-3C2E-8B1D",
        "GPGKEY-UTIL-PRO-VIP",
        "实用工具-激活码-2025",
        "123",
        "王刚",
        "wanggang"
    };

    private readonly IServiceScopeFactory _scopeFactory;

    public ActivationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// 检查程序是否已激活
    /// </summary>
    public bool IsActivated()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        return db.ActivationRecord.Any();
    }

    /// <summary>
    /// 验证激活码并激活
    /// </summary>
    /// <param name="code">用户输入的激活码</param>
    /// <returns>激活是否成功</returns>
    public bool Activate(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        // 检查是否已经激活
        if (IsActivated())
            return true;

        // 验证激活码是否在预设列表中
        if (!ValidActivationCodes.Contains(code.Trim()))
            return false;

        // 保存激活记录到数据库
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        db.ActivationRecord.Add(new ActivationRecord
        {
            ActivationCode = code.Trim(),
            ActivateTime = DateTime.Now,
            CreateTime = DateTime.Now
        });
        db.SaveChanges();

        return true;
    }
}
