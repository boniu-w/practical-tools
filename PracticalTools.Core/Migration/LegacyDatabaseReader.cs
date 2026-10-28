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
    public static MigrationSnapshot Read(string databasePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("旧库导出需在原 Windows 用户账户下进行；其他平台请选择加密迁移包导入。");
        var header = new byte[16];
        using (var file = File.OpenRead(databasePath)) file.ReadExactly(header);
        var plaintext = header.AsSpan().SequenceEqual("SQLite format 3\0"u8);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            Password = plaintext ? "" : CommonConst.DbPassword
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using (var identify = connection.CreateCommand())
        {
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
        foreach (var key in snapshot.Keys)
        {
            if (key.Passphrase.StartsWith("pt-aesgcm:", StringComparison.Ordinal))
                throw new InvalidDataException("此库使用新版口令保护，请从新版应用导出迁移包。");
            key.Passphrase = DecodePassphrase(key.Passphrase);
        }
        KeyLibrary.ValidateMigrationKeys(snapshot);
        transaction.Commit();
        return snapshot;
    }

    private static string DecodePassphrase(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(stored); }
        catch (FormatException) { return stored; }
        try
        {
            // DPAPI's version/provider header distinguishes a protected blob from old plaintext.
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

    private static List<T> ReadTable<T>(SqliteConnection connection, SqliteTransaction transaction, string table) where T : new()
    {
        using var exists = connection.CreateCommand();
        exists.Transaction = transaction;
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name";
        exists.Parameters.AddWithValue("$name", table);
        if (Convert.ToInt64(exists.ExecuteScalar()) == 0) return [];
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Table identifiers are a fixed allowlist at the call sites, never user input.
        command.CommandText = $"SELECT * FROM [{table}]";
        using var reader = command.ExecuteReader();
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
