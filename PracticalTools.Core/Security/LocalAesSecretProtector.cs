using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PracticalTools.Security;

/// <summary>Versioned AES-256-GCM values. Never creates a replacement key during encryption/decryption.</summary>
public sealed class LocalAesSecretProtector : ISecretProtector
{
    public const string Prefix = "pt-aesgcm:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("PracticalTools/GpgFile.Passphrase/v1");
    private readonly string _keyPath;

    public LocalAesSecretProtector(string keyPath) => _keyPath = Path.GetFullPath(keyPath);

    /// <summary>Only the startup initializer may create a key, after checking for existing key records.</summary>
    public void InitializeKey(bool allowCreate)
    {
        if (!File.Exists(_keyPath))
        {
            if (!allowCreate)
                throw new CryptographicException("口令保护密钥文件缺失，请恢复与数据库配套的 secret-protection.key；不会自动生成替代密钥。");
            CreateKey();
        }
        var key = ReadKey();
        CryptographicOperations.ZeroMemory(key);
    }

    private void CreateKey()
    {
        var key = RandomNumberGenerator.GetBytes(KeySize);
        var temporary = _keyPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = CreatePrivateFile(temporary))
            {
                stream.Write(key);
                stream.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, _keyPath, overwrite: false); }
            catch (IOException) when (File.Exists(_keyPath)) { /* A concurrent initializer published its complete key. */ }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static FileStream CreatePrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new InvalidOperationException("无法确定当前 Windows 用户。");
            var security = new FileSecurity();
            security.SetOwner(owner);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.None, security);
        }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
    }

    private byte[] ReadKey()
    {
        // Read on every operation: deletion or replacement must not be hidden by a cached key.
        using var stream = new FileStream(_keyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != KeySize) throw new CryptographicException("口令保护密钥文件格式无效，请恢复正确备份。");
        var key = new byte[KeySize];
        try { stream.ReadExactly(key); return key; }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (plaintext.Length == 0) return "";
        var key = ReadKey();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var payload = new byte[NonceSize + TagSize + plainBytes.Length];
            var nonce = payload.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plainBytes, payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize), Purpose);
            return Prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        if (protectedText.Length == 0) return "";
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("口令格式不受支持；旧 Windows DPAPI 数据需通过迁移流程导入。");
        byte[] payload;
        try { payload = Convert.FromBase64String(protectedText[Prefix.Length..]); }
        catch (FormatException ex) { throw new CryptographicException("受保护口令格式无效。", ex); }
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("受保护口令不完整。");
        var key = ReadKey();
        var plaintext = new byte[payload.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize), plaintext, Purpose);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
