using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PracticalTools.Security;

/// <summary>带版本号的 AES-256-GCM 口令保护实现；加解密时绝不会自动生成替代密钥。</summary>
public sealed class LocalAesSecretProtector : ISecretProtector
{
    /// <summary>受保护口令的版本前缀，用于区分旧版 DPAPI 数据。</summary>
    public const string Prefix = "pt-aesgcm:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("PracticalTools/GpgFile.Passphrase/v1");
    private readonly string _keyPath;

    /// <summary>创建口令保护器。</summary>
    /// <param name="keyPath">与数据库配套的密钥文件路径，会规范化为绝对路径。</param>
    public LocalAesSecretProtector(string keyPath) => _keyPath = Path.GetFullPath(keyPath);

    /// <summary>
    /// 在启动时检查密钥文件：仅允许初始化器在确认没有既有加密记录后创建新密钥。
    /// 文件已存在时只验证其可读且长度正确。
    /// </summary>
    /// <param name="allowCreate">密钥文件缺失时是否允许创建新密钥。</param>
    /// <exception cref="CryptographicException">密钥缺失且不允许创建，或密钥格式无效。</exception>
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

    /// <summary>
    /// 生成随机 256 位密钥，写入权限受限的临时文件后再移动到正式路径。
    /// 并发初始化时，若目标已被其他实例写入完整密钥，则保留对方结果。
    /// </summary>
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
            catch (IOException) when (File.Exists(_keyPath)) { /* 并发的初始化器已写入完整密钥，保留对方结果。 */ }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>创建仅当前用户可读写的密钥文件：Windows 使用受限 ACL，其他平台使用 0600 权限。</summary>
    /// <param name="path">要创建的文件路径，必须尚不存在。</param>
    /// <returns>以独占方式打开的写入流。</returns>
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

    /// <summary>每次加解密都重新读取密钥文件，避免缓存掩盖密钥被删除或替换。</summary>
    /// <returns>32 字节密钥，调用方负责使用后清零。</returns>
    /// <exception cref="CryptographicException">密钥文件长度无效。</exception>
    private byte[] ReadKey()
    {
        // 每次操作都重新读取：密钥被删除或替换不能被缓存掩盖。
        using var stream = new FileStream(_keyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != KeySize) throw new CryptographicException("口令保护密钥文件格式无效，请恢复正确备份。");
        var key = new byte[KeySize];
        try { stream.ReadExactly(key); return key; }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    /// <summary>把明文口令加密为带 <c>pt-aesgcm:v1:</c> 前缀的 Base64 文本；空字符串原样返回。</summary>
    /// <param name="plaintext">要保护的明文口令。</param>
    /// <returns>受保护字符串，空输入返回空字符串。</returns>
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (plaintext.Length == 0) return "";
        var key = ReadKey();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            // 负载布局：随机数(12) + 认证标签(16) + 密文。
            var payload = new byte[NonceSize + TagSize + plainBytes.Length];
            var nonce = payload.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            // Purpose 作为附加认证数据，防止密文被挪用到其他用途。
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

    /// <summary>解密受保护口令；仅接受当前版本前缀，旧版 DPAPI 数据需通过迁移流程导入。</summary>
    /// <param name="protectedText">受保护字符串。</param>
    /// <returns>明文口令，空输入返回空字符串。</returns>
    /// <exception cref="CryptographicException">前缀、Base64、长度、密钥或认证标签无效。</exception>
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
