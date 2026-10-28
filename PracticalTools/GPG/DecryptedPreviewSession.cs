using System.Collections.Concurrent;
using System.IO;
using Serilog;

namespace PracticalTools.GPG;

/// <summary>每次预览独占一个临时目录；绝不使用 PGP 文件名作为输出路径。</summary>
public sealed class DecryptedPreviewSession : IDisposable
{
    private static readonly ConcurrentDictionary<string, DecryptedPreviewSession> Active = new();
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "PracticalTools", "DecryptedPreview");
    private bool _disposed;

    public string DirectoryPath { get; }
    public string ContentDirectory { get; }
    public string FilePath { get; private set; }
    public string OriginalFileName { get; private set; } = "";

    private DecryptedPreviewSession()
    {
        DirectoryPath = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        ContentDirectory = Path.Combine(DirectoryPath, "content");
        Directory.CreateDirectory(ContentDirectory);
        FilePath = Path.Combine(ContentDirectory, "payload");
        Active.TryAdd(DirectoryPath, this);
    }

    public static async Task<DecryptedPreviewSession> CreateAsync(string encryptedPath,
        GpgFile key, CancellationToken cancellationToken = default)
    {
        var session = new DecryptedPreviewSession();
        try
        {
            var name = await GpgHelper.DecryptFileForPreviewAsync(encryptedPath, session.FilePath,
                key.SecretKey, key.Passphrase, cancellationToken).ConfigureAwait(false);
            // 重命名密文后仍优先使用加密包内的原始扩展名，但不信任其中的目录信息。
            name = Path.GetFileName(name.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileName(encryptedPath);
                if (name.EndsWith(".gpg", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            }
            session.OriginalFileName = name;
            string extension = Path.GetExtension(name);
            if (extension.Length > 16 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                extension = "";
            string target = Path.Combine(session.ContentDirectory, "preview" + extension.ToLowerInvariant());
            File.Move(session.FilePath, target);
            session.FilePath = target;
            cancellationToken.ThrowIfCancellationRequested();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        // WebView2 退出后才会释放配置目录，允许在关闭浏览器后的短暂间隔内重试。
        for (int attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
                _disposed = true;
                Active.TryRemove(DirectoryPath, out _);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 11)
                    Log.Warning(ex, "预览临时目录暂时无法清理: {Directory}", DirectoryPath);
                else
                    Thread.Sleep(150);
            }
        }
    }

    public void DeleteDecryptedContent()
    {
        try
        {
            if (Directory.Exists(ContentDirectory)) Directory.Delete(ContentDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "预览内容等待浏览器释放后重试清理: {Directory}", ContentDirectory);
        }
    }

    public static void CleanupAll()
    {
        foreach (var session in Active.Values) session.Dispose();
    }

    /// <summary>应用启动时清理上次异常退出遗留的预览文件。</summary>
    public static void CleanupPreviousSessions()
    {
        if (!Directory.Exists(Root)) return;
        foreach (string directory in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "无法清理上次预览目录: {Directory}", directory);
            }
        }
    }
}
