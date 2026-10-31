using System.Security.Cryptography;
using System.Text.Json;
using PracticalTools.GPG;

namespace PracticalTools.Migration;

/// <summary>加密迁移包的导出、预览与导入；包体使用口令派生密钥的 AES-GCM 加密。</summary>
public sealed class MigrationPackageService(KeyLibrary library)
{
    private static readonly byte[] Magic = "PTMIG01\n"u8.ToArray();
    // 头部布局：Magic(8) + 盐(16) + 随机数(12) + 认证标签(16)，其后为密文。
    private const int HeaderSize = 8 + 16 + 12 + 16;
    private const int MaxBytes = 64 * 1024 * 1024;

    /// <summary>读取旧版 SQLite 数据库，并导出为加密迁移包。</summary>
    /// <param name="source">旧库文件路径。</param>
    /// <param name="target">迁移包输出路径。</param>
    /// <param name="password">至少 12 个字符的迁移口令。</param>
    public Task ExportLegacyAsync(string source, string target, string password) => Task.Run(() =>
        WritePackage(LegacyDatabaseReader.Read(source), target, password));

    /// <summary>从当前密钥库创建快照，并导出为加密迁移包。</summary>
    /// <param name="target">迁移包输出路径。</param>
    /// <param name="password">至少 12 个字符的迁移口令。</param>
    public async Task ExportCurrentAsync(string target, string password)
    {
        var snapshot = await library.CreateMigrationSnapshotAsync().ConfigureAwait(false);
        await Task.Run(() => WritePackage(snapshot, target, password)).ConfigureAwait(false);
    }

    /// <summary>解密并校验迁移包，返回记录数量、密钥指纹和内容摘要，供导入前确认。</summary>
    /// <param name="path">迁移包路径。</param>
    /// <param name="password">迁移口令。</param>
    /// <returns>预览信息；<c>Digest</c> 用于导入时确认迁移包未被替换。</returns>
    public Task<MigrationPreview> PreviewAsync(string path, string password) => Task.Run(() =>
    {
        var (snapshot, digest) = ReadPackage(path, password);
        var keys = KeyLibrary.ValidateMigrationKeys(snapshot);
        return new MigrationPreview(digest, snapshot.Keys.Count, snapshot.Blacklist.Count, snapshot.Scans.Count,
            snapshot.Files.Count, keys.Select(k => GpgHelper.GetFingerprint(k.PublicRing)).ToArray());
    });

    /// <summary>重新读取迁移包，确认摘要与预览时一致后再导入，避免导入被替换的文件。</summary>
    /// <param name="path">迁移包路径。</param>
    /// <param name="password">迁移口令。</param>
    /// <param name="expectedDigest">预览阶段返回的包内容摘要。</param>
    /// <returns>导入结果。</returns>
    /// <exception cref="InvalidDataException">迁移包在预览后发生变化。</exception>
    public async Task<MigrationResult> ImportAsync(string path, string password, string expectedDigest)
    {
        var (snapshot, digest) = await Task.Run(() => ReadPackage(path, password)).ConfigureAwait(false);
        if (!string.Equals(digest, expectedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("迁移包已在预览后改变，请重新预览。");
        return await library.ImportMigrationSnapshotAsync(snapshot).ConfigureAwait(false);
    }

    /// <summary>迁移口令至少 12 个字符，且不能全为空白。</summary>
    private static void CheckPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new ArgumentException("迁移口令至少需要 12 个字符。");
    }

    /// <summary>
    /// 校验快照与口令后，把快照序列化为 JSON 并用 AES-GCM 加密写入目标路径。
    /// 先写同目录临时文件再移动，失败时删除临时文件，并清零明文和密钥缓冲。
    /// </summary>
    private static void WritePackage(MigrationSnapshot snapshot, string path, string password)
    {
        CheckPassword(password);
        KeyLibrary.ValidateMigrationKeys(snapshot);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        byte[]? key = null;
        try
        {
            if (plaintext.Length > MaxBytes - HeaderSize) throw new InvalidDataException("迁移包超过 64 MiB 限制。");
            var payload = new byte[HeaderSize + plaintext.Length];
            Magic.CopyTo(payload, 0);
            RandomNumberGenerator.Fill(payload.AsSpan(8, 16));
            RandomNumberGenerator.Fill(payload.AsSpan(24, 12));
            // PBKDF2-SHA256 迭代 60 万次，增加弱口令被暴力破解的成本。
            key = Rfc2898DeriveBytes.Pbkdf2(password, payload.AsSpan(8, 16), 600000, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, 16);
            // 以文件头 Magic 作为附加认证数据，防止格式/版本标识被篡改。
            aes.Encrypt(payload.AsSpan(24, 12), plaintext, payload.AsSpan(HeaderSize), payload.AsSpan(36, 16), Magic);
            var target = Path.GetFullPath(path);
            var temporary = target + $".{Guid.NewGuid():N}.tmp";
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                // 非 Windows 平台仅允许当前用户读写。
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporary, options)) { stream.Write(payload); stream.Flush(true); }
                // 临时文件与目标同目录，移动后不会留下半截迁移包。
                File.Move(temporary, target, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (key != null) CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// 读取、验证并解密迁移包，反序列化后校验数据结构。
    /// </summary>
    /// <returns>迁移快照，以及整个包文件的 SHA-256 十六进制摘要。</returns>
    /// <exception cref="InvalidDataException">长度、格式或数据内容无效。</exception>
    /// <exception cref="CryptographicException">口令错误或包已损坏。</exception>
    private static (MigrationSnapshot Snapshot, string Digest) ReadPackage(string path, string password)
    {
        CheckPassword(password);
        using var file = File.OpenRead(path);
        if (file.Length < HeaderSize || file.Length > MaxBytes) throw new InvalidDataException("迁移包长度无效，最大支持 64 MiB。");
        var payload = new byte[(int)file.Length];
        file.ReadExactly(payload);
        if (!payload.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("迁移包格式或版本不受支持。");
        var plaintext = new byte[payload.Length - HeaderSize];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, payload.AsSpan(8, 16), 600000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            try { aes.Decrypt(payload.AsSpan(24, 12), payload.AsSpan(HeaderSize), payload.AsSpan(36, 16), plaintext, Magic); }
            catch (CryptographicException) { throw new CryptographicException("迁移口令错误或迁移包已损坏，未导入任何数据。"); }
            var snapshot = JsonSerializer.Deserialize<MigrationSnapshot>(plaintext) ?? throw new InvalidDataException("迁移包中没有有效数据。");
            snapshot.ValidateShape();
            // 对整个文件计算摘要，导入前用它确认迁移包未被替换。
            return (snapshot, Convert.ToHexString(SHA256.HashData(payload)));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(key); }
    }
}
