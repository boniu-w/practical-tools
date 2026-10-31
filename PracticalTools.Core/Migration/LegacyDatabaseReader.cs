using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PracticalTools.Blacklist.Model;
using PracticalTools.Constant;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.FileManager.Model;
using PracticalTools.GPG;

namespace PracticalTools.Migration;

public static class LegacyDatabaseReader
{
    /// <summary>
    /// 读取旧版（WPF）应用的 SQLite 数据库并转换为迁移快照。
    /// 仅支持 Windows：旧库口令可能由当前用户的 DPAPI 保护，必须用原账户解开。
    /// </summary>
    /// <param name="databasePath">旧库文件路径。</param>
    /// <returns>包含密钥、黑名单、查重扫描和文件记录的迁移快照。</returns>
    /// <exception cref="PlatformNotSupportedException">在非 Windows 平台上调用。</exception>
    /// <exception cref="InvalidDataException">不是已知旧库、使用新版口令保护或记录数超过限制。</exception>
    /// <exception cref="CryptographicException">DPAPI 口令无法用当前用户解开。</exception>
    public static MigrationSnapshot Read(string databasePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("旧库导出需在原 Windows 用户账户下进行；其他平台请选择加密迁移包导入。");
        var header = new byte[16];
        using (var file = File.OpenRead(databasePath)) file.ReadExactly(header);
        // 未加密的 SQLite 文件以固定 16 字节头开始；否则按加密库处理。
        var plaintext = header.AsSpan().SequenceEqual("SQLite format 3\0"u8);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            Password = plaintext ? "" : CommonConst.DbPassword
        }.ToString());
        connection.Open();
        // 延迟事务保证多张表读取到同一时刻的一致快照。
        using var transaction = connection.BeginTransaction(deferred: true);
        using (var identify = connection.CreateCommand())
        {
            // 至少要存在一张已知数据表，才认为是实用工具的旧库。
            identify.Transaction = transaction;
            identify.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('GpgFile','Blacklist','DuplicateFiles','MyFile')";
            if (Convert.ToInt64(identify.ExecuteScalar()) == 0)
                throw new InvalidDataException("所选数据库不包含已知的实用工具数据表。");
        }
        var snapshot = new MigrationSnapshot
        {
            Keys = ReadTable<GpgFile>(connection, transaction, "GpgFile"),
            Blacklist = ReadTable<BlacklistEntry>(connection, transaction, "Blacklist"),
            Scans = ReadTable<DuplicateFilesEntry>(connection, transaction, "DuplicateFiles"),
            Files = ReadTable<MyFile>(connection, transaction, "MyFile")
        };
        // 新版口令使用 pt-aesgcm: 前缀，旧版应用无法解析，直接拒绝。
        foreach (var key in snapshot.Keys)
        {
            if (key.Passphrase.StartsWith("pt-aesgcm:", StringComparison.Ordinal))
                throw new InvalidDataException("此库使用新版口令保护，请从新版应用导出迁移包。");
            // 旧版口令可能是 DPAPI 密文，导出前尽量还原为明文。
            key.Passphrase = DecodePassphrase(key.Passphrase);
        }
        KeyLibrary.ValidateMigrationKeys(snapshot);
        // 只读事务，提交仅表示一致性读取结束。
        transaction.Commit();
        return snapshot;
    }

    /// <summary>
    /// 解码旧库中的口令字段：空值原样返回，非 Base64 视为旧版明文；
    /// 带 DPAPI 头的 Base64 密文则用当前 Windows 用户的 DPAPI 解开。
    /// </summary>
    /// <param name="stored">数据库中保存的口令字符串。</param>
    /// <returns>明文口令，或无法识别时返回原始字符串。</returns>
    /// <exception cref="CryptographicException">DPAPI 解密失败。</exception>
    private static string DecodePassphrase(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(stored); }
        catch (FormatException) { return stored; }
        try
        {
            // DPAPI 的版本/提供程序头用于区分受保护数据块和旧版明文。
            if (!bytes.AsSpan().StartsWith(Convert.FromHexString("01000000D08C9DDF0115D1118C7A00C04FC297EB"))) return stored;
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            byte[] plain;
            try { plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser); }
            catch (CryptographicException) { throw new CryptographicException("无法解开旧 DPAPI 口令，请使用原 Windows 用户和完整旧库；不会把密文作为明文导出。"); }
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>
    /// 读取指定表的全部行并映射到 <typeparamref name="T"/> 的同名可写属性。
    /// 表不存在时返回空列表；表名由调用方固定传入，绝不来自用户输入。
    /// </summary>
    /// <typeparam name="T">具有无参构造函数的目标记录类型。</typeparam>
    /// <param name="connection">已打开的只读数据库连接。</param>
    /// <param name="transaction">读取事务，保证各表数据一致。</param>
    /// <param name="table">固定白名单中的表名。</param>
    /// <returns>映射后的记录列表。</returns>
    /// <exception cref="InvalidDataException">记录数超过 100000 条。</exception>
    private static List<T> ReadTable<T>(SqliteConnection connection, SqliteTransaction transaction, string table) where T : new()
    {
        using var exists = connection.CreateCommand();
        exists.Transaction = transaction;
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name";
        exists.Parameters.AddWithValue("$name", table);
        if (Convert.ToInt64(exists.ExecuteScalar()) == 0) return [];
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // 表名在调用处来自固定白名单，不是用户输入。
        command.CommandText = $"SELECT * FROM [{table}]";
        using var reader = command.ExecuteReader();
        // 列名按不区分大小写匹配，兼容旧库大小写不一致的列名。
        var columns = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, i => i, StringComparer.OrdinalIgnoreCase);
        var properties = typeof(T).GetProperties().Where(p => p.CanWrite && p.GetCustomAttribute<NotMappedAttribute>() == null
            && columns.ContainsKey(p.Name)).ToList();
        var results = new List<T>();
        while (reader.Read())
        {
            if (results.Count >= 100000) throw new InvalidDataException("旧库记录数量超过单次迁移限制。");
            var record = new T();
            foreach (var property in properties)
            {
                var raw = reader.GetValue(columns[property.Name]);
                if (raw is DBNull) continue;
                // 旧库用文本存时间、用整数存布尔值，这里统一转换为目标属性类型。
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                object value = type == typeof(DateTime) ? DateTime.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)
                    : type == typeof(bool) ? Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0
                    : Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
                property.SetValue(record, value);
            }
            results.Add(record);
        }
        return results;
    }
}
