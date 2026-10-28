using System.Security.Cryptography;
using System.Text.Json;
using PracticalTools.GPG;

namespace PracticalTools.Migration;

public sealed class MigrationPackageService(KeyLibrary library)
{
    private static readonly byte[] Magic = "PTMIG01\n"u8.ToArray();
    private const int HeaderSize = 8 + 16 + 12 + 16;
    private const int MaxBytes = 64 * 1024 * 1024;

    public Task ExportLegacyAsync(string source, string target, string password) => Task.Run(() =>
        WritePackage(LegacyDatabaseReader.Read(source), target, password));

    public async Task ExportCurrentAsync(string target, string password)
    {
        var snapshot = await library.CreateMigrationSnapshotAsync().ConfigureAwait(false);
        await Task.Run(() => WritePackage(snapshot, target, password)).ConfigureAwait(false);
    }

    public Task<MigrationPreview> PreviewAsync(string path, string password) => Task.Run(() =>
    {
        var (snapshot, digest) = ReadPackage(path, password);
        var keys = KeyLibrary.ValidateMigrationKeys(snapshot);
        return new MigrationPreview(digest, snapshot.Keys.Count, snapshot.Blacklist.Count, snapshot.Scans.Count,
            snapshot.Files.Count, keys.Select(k => GpgHelper.GetFingerprint(k.PublicRing)).ToArray());
    });

    public async Task<MigrationResult> ImportAsync(string path, string password, string expectedDigest)
    {
        var (snapshot, digest) = await Task.Run(() => ReadPackage(path, password)).ConfigureAwait(false);
        if (!string.Equals(digest, expectedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("迁移包已在预览后改变，请重新预览。");
        return await library.ImportMigrationSnapshotAsync(snapshot).ConfigureAwait(false);
    }

    private static void CheckPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new ArgumentException("迁移口令至少需要 12 个字符。");
    }

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
            key = Rfc2898DeriveBytes.Pbkdf2(password, payload.AsSpan(8, 16), 600000, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(payload.AsSpan(24, 12), plaintext, payload.AsSpan(HeaderSize), payload.AsSpan(36, 16), Magic);
            var target = Path.GetFullPath(path);
            var temporary = target + $".{Guid.NewGuid():N}.tmp";
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporary, options)) { stream.Write(payload); stream.Flush(true); }
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
            return (snapshot, Convert.ToHexString(SHA256.HashData(payload)));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(key); }
    }
}
