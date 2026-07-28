using System.IO;
using Microsoft.Data.Sqlite;
using PracticalTools.Constant;

namespace PracticalTools.Sqlite;

/// <summary>
/// 将旧版未加密的 SQLite 数据库迁移到 SQLCipher 加密格式
/// </summary>
public static class DatabaseMigrationHelper
{
    /// <summary>
    /// 检测并迁移旧版未加密数据库。
    /// 如果数据库已是加密格式则直接返回。
    /// </summary>
    public static void MigrateToEncrypted(string dbPath)
    {
        // 文件不存在，无需迁移
        if (!File.Exists(dbPath))
            return;

        // 尝试用密码打开——成功说明已加密
        var encryptedConnStr = $"Data Source={dbPath};Password={CommonConst.DbPassword}";
        try
        {
            using var testConn = new SqliteConnection(encryptedConnStr);
            testConn.Open();
            return; // 已是加密格式
        }
        catch
        {
            // 打不开说明是旧版未加密数据库
        }

        // 打开旧版未加密数据库，用 PRAGMA rekey 原地加密
        var plainConnStr = $"Data Source={dbPath}";
        using var conn = new SqliteConnection(plainConnStr);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA rekey = '{CommonConst.DbPassword}'";
        cmd.ExecuteNonQuery();

        // VACUUM 重写所有数据页，确保整个文件被加密
        cmd.CommandText = "VACUUM";
        cmd.ExecuteNonQuery();
    }
}
