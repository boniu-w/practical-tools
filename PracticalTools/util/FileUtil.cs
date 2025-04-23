using System.IO;
using System.Security.Cryptography;

namespace PracticalTools.util;

public class FileUtil
{
    /// <summary>
    /// md5 较快
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static byte[] ComputeFileHashMD5(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The specified file does not exist.", filePath);

        // 使用 MD5 算法
        MD5 md5 = MD5.Create();
        return Md5Hash(filePath, md5);
    }

    public static byte[] Md5Hash(string filePath, MD5 md5)
    {
        // 自动释放文件流资源
        using FileStream stream = File.OpenRead(filePath);

        // 动态计算缓冲区大小（保持与 Java 相同逻辑）
        long bufferSize = CalculateBufferSize(new FileInfo(filePath).Length);
        byte[] buffer = new byte[bufferSize];

        int bytesRead;
        // 分块读取文件更新哈希
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        // 完成最终哈希计算
        md5.TransformFinalBlock(buffer, 0, 0);
        return md5.Hash!;
    }

    /// <summary>
    /// sha256 较慢
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static byte[] ComputeFileHashSHA256(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The specified file does not exist.", filePath);

        // 使用 SHA256 算法
        using SHA256 sha256 = SHA256.Create();
        return Sha256Hash();

        byte[] Sha256Hash()
        {
            // 自动释放文件流资源
            using FileStream stream = File.OpenRead(filePath);

            // 动态计算缓冲区大小（保持与 Java 相同逻辑）
            long bufferSize = CalculateBufferSize(new FileInfo(filePath).Length);
            byte[] buffer = new byte[bufferSize];

            int bytesRead;
            // 分块读取文件更新哈希
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            // 完成最终哈希计算
            sha256.TransformFinalBlock(buffer, 0, 0);
            return sha256.Hash!;
        }
    }

    private static long CalculateBufferSize(long fileLength)
    {
        // 缓冲区大小逻辑（512B - 64KB）
        return Math.Clamp(fileLength, 512, 65536);
    }

    /// <summary>
    /// 自己指定算法
    /// </summary>
    /// <param name="file"></param>
    /// <param name="algorithm"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static byte[] GetFileBytes(FileInfo file, string algorithm)
    {
        if (!file.Exists)
            throw new FileNotFoundException("The specified file does not exist.");

        // 规范化算法名称（例如将"SHA-256"转换为"SHA256"）
        string normalizedAlgorithm = algorithm.Replace("-", "");
        using HashAlgorithm hashAlgorithm = HashAlgorithm.Create(normalizedAlgorithm)
                                            ?? throw new ArgumentException($"Unsupported hash algorithm: {algorithm}", nameof(algorithm));

        using FileStream stream = file.OpenRead();

        // 动态计算缓冲区大小（512B-64KB）
        long bufferSize = Math.Clamp(file.Length, 512L, 65536L);
        byte[] buffer = new byte[(int)bufferSize];

        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        // 最终完成哈希计算
        hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return hashAlgorithm.Hash ?? Array.Empty<byte>();
    }

    /// <summary>
    /// 带进度条
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="algorithm">算法{md5, sha1, sha256}</param>
    /// <param name="progressCallback"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static byte[] GetFileHashWithProgress(string filePath, string algorithm, Action<double>? progressCallback = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The specified file does not exist.", filePath);

        using var stream = File.OpenRead(filePath);
        using var hashAlgorithm = HashAlgorithm.Create(algorithm)
                                  ?? throw new ArgumentException($"Unsupported algorithm: {algorithm}");

        // 缓冲区大小逻辑（512B - 64KB）
        long bufferSize = CalculateBufferSize(new FileInfo(filePath).Length);
        byte[] buffer = new byte[bufferSize];
        long totalRead = 0;
        long totalLength = stream.Length;

        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalRead += bytesRead;
            progressCallback?.Invoke(totalRead * 1.0 / totalLength);
        }

        hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return hashAlgorithm.Hash!;
    }

    /// <summary>
    /// 带进度条
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="algorithm"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static byte[] GetFileHashWithBuffer(string filePath, string algorithm)
    {
        // 确保文件路径有效并且文件存在
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The specified file does not exist.", filePath);

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hashAlgorithm = HashAlgorithm.Create(algorithm)
                                  ?? throw new ArgumentException($"Unsupported algorithm: {algorithm}");

        // 缓冲区大小逻辑（512B - 64KB）
        long bufferSize = CalculateBufferSize(new FileInfo(filePath).Length);
        byte[] buffer = new byte[bufferSize];
        int bytesRead;
        long totalRead = 0;
        long totalLength = stream.Length;

        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalRead += bytesRead;

            // 可选：显示进度
            double progress = (double)totalRead / totalLength * 100;
            Console.WriteLine($"Progress: {progress:F2}%");
        }

        // 最后调用 TransformFinalBlock 来结束哈希计算
        hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return hashAlgorithm.Hash!;
    }

    /// <summary>
    /// 扫描文件夹下所有文件
    /// </summary>
    /// <param name="path"></param>
    /// <param name="fileList"></param>
    /// <returns></returns>
    public static List<string> GetAllFiles(string path, List<string>? fileList = null)
    {
        fileList ??= new List<string>();

        if (!Directory.Exists(path))
            return fileList;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (File.Exists(entry))
                {
                    fileList.Add(entry);
                }
                else if (Directory.Exists(entry))
                {
                    GetAllFiles(entry, fileList); // 递归子目录
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied to: {path} - {ex.Message}");
        }

        return fileList;
    }

    /// <summary>
    /// 忽略系统文件, 不忽略隐藏
    /// 筛选特定后缀（如 .txt, .jpg）
    /// </summary>
    /// <param name="path"></param>
    /// <param name="fileList"></param>
    /// <param name="boolIgnore">是否忽略系统文件</param>
    /// <param name="allowedExtensions"></param>
    /// <returns></returns>
    public static List<string> GetAllFiles(
        string path,
        List<string>? fileList = null,
        bool boolIgnore = true,
        string[]? allowedExtensions = null)
    {
        fileList ??= new List<string>();
        allowedExtensions ??= Array.Empty<string>();

        if (!Directory.Exists(path))
            return fileList;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attr = File.GetAttributes(entry);

                // 忽略系统文件或目录 
                // 不忽略隐藏 attr.HasFlag(FileAttributes.Hidden)
                if (attr.HasFlag(FileAttributes.System))
                    if (boolIgnore)
                    {
                        continue;
                    }

                if (File.Exists(entry))
                {
                    if (allowedExtensions.Length == 0 || HasAllowedExtension(entry, allowedExtensions))
                    {
                        fileList.Add(entry);
                    }
                }
                else if (Directory.Exists(entry))
                {
                    GetAllFiles(entry, fileList, boolIgnore, allowedExtensions); // 递归
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied to: {path} - {ex.Message}");
        }

        return fileList;
    }

    /// <summary>
    /// 判断是否是 指定后缀格式的文件
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="allowedExtensions"></param>
    /// <returns></returns>
    private static bool HasAllowedExtension(string filePath, string[] allowedExtensions)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        foreach (var allowed in allowedExtensions)
        {
            if (ext == allowed.ToLowerInvariant())
                return true;
        }

        return false;
    }
}