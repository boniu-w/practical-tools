using System.IO;
using System.Threading;
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

        // 已是加密格式则直接返回：加密库文件头是随机字节，明文库以 "SQLite format 3" 开头。
        // 注意不要用「带密码连接尝试打开」来探测——打开失败会在 SqliteConnectionInternal 构造期泄漏原生句柄，导致文件被占用无法替换。
        if (!IsPlaintextDatabase(dbPath))
            return;

        // 用 sqlcipher_export 将明文库导出为加密库，再替换原文件。
        // 注意：PRAGMA rekey 无法对未加密的库生效（实测会保持明文），必须用 sqlcipher_export。
        string tempPath = dbPath + ".enc.tmp";
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var plainConnStr = $"Data Source={dbPath}";
            using var conn = new SqliteConnection(plainConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"ATTACH DATABASE '{tempPath}' AS encrypted KEY '{CommonConst.DbPassword}'; " +
                "SELECT sqlcipher_export('encrypted'); " +
                "DETACH DATABASE encrypted;";
            cmd.ExecuteNonQuery();
        }
        finally
        {
            // 释放连接池对原文件的占用，便于后续替换文件
            SqliteConnection.ClearAllPools();
        }

        // 替换原文件（先保留明文备份，替换成功后删除）
        string backupPath = dbPath + ".plain.bak";
        if (File.Exists(backupPath))
            File.Delete(backupPath);

        ReplaceFile(dbPath, backupPath);
        ReplaceFile(tempPath, dbPath);
        File.Delete(backupPath);
    }

    /// <summary>
    /// 判断文件是否为明文 SQLite 数据库（文件头以 "SQLite format 3" 开头）。
    /// </summary>
    private static bool IsPlaintextDatabase(string dbPath)
    {
        try
        {
            byte[] header = new byte[16];
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            int read = fs.Read(header, 0, header.Length);
            if (read < 16)
                return false; // 文件过小，视为异常/已加密，跳过迁移

            return header[0] == (byte)'S' && header[1] == (byte)'Q' && header[2] == (byte)'L'
                && header[3] == (byte)'i' && header[4] == (byte)'t' && header[5] == (byte)'e';
        }
        catch
        {
            return false; // 读取失败，视为已加密，跳过迁移
        }
    }

    /// <summary>
    /// 带重试的文件移动，规避偶发的文件占用。
    /// </summary>
    private static void ReplaceFile(string source, string dest)
    {
        const int maxRetries = 5;
        for (int i = 0; ; i++)
        {
            try
            {
                File.Move(source, dest);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                Thread.Sleep(200 * (i + 1));
            }
        }
    }
}
