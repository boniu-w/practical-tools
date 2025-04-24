using System.IO;
using System.Security.Cryptography;
using Serilog;

namespace PracticalTools.util;

public class FileUtil
{
    public ValueTuple<string, string, string> FileValueTuple { get; set; }

    // 初始化日志（仅在 FileUtil 类中使用）
    static FileUtil()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console() // 控制台输出
            .WriteTo.File("logs/fileutil-log-.txt", rollingInterval: RollingInterval.Day) // 文件日志，按天归档
            .CreateLogger();
    }

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
            Log.Error($"Access denied to: {path} - {ex.Message}");
        }

        return fileList;
    }

    // public static List<(string, long, DateTime)> GetAllFiles(string path, List<(string, long, DateTime)>? fileList = null)
    // {
    //     fileList ??= new List<(string, long, DateTime)>();
    //
    //     if (!Directory.Exists(path))
    //         return fileList;
    //
    //     try
    //     {
    //         foreach (var entry in Directory.EnumerateFileSystemEntries(path))
    //         {
    //             if (File.Exists(entry))
    //             {
    //                 FileInfo fileInfo = new FileInfo(entry);
    //                 fileList.Add((entry, fileInfo.Length, fileInfo.LastWriteTime));
    //             }
    //             else if (Directory.Exists(entry))
    //             {
    //                 GetAllFiles(entry, fileList); // 递归子目录
    //             }
    //         }
    //     }
    //     catch (UnauthorizedAccessException ex)
    //     {
    //         Log.Error($"Access denied to: {path} - {ex.Message}");
    //     }
    //
    //     return fileList;
    // }

    /// <summary>
    /// 和上方方法 List<(string, long, DateTime)> GetAllFiles(string path, List<(string, long, DateTime)>? fileList = null) 一样
    /// 但建议用这个
    /// </summary>
    /// <param name="path"></param>
    /// <param name="fileList"></param>
    /// <returns></returns>
    public static List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)> GetAllFiles(
        string path,
        List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)>? fileList = null)
    {
        fileList ??= new List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)>();

        if (!Directory.Exists(path))
            return fileList;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (File.Exists(entry))
                {
                    FileInfo fileInfo = new FileInfo(entry);

                    fileList.Add((Path.GetFileName(entry), entry, fileInfo.Length, fileInfo.CreationTime, fileInfo.LastWriteTime));
                }
                else if (Directory.Exists(entry))
                {
                    GetAllFiles(entry, fileList); // 递归子目录
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error($"Access denied to: {path} - {ex.Message}");
        }

        return fileList;
    }

    /// <summary>
    /// ValueTuple 形式
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static List<ValueTuple<string, long, DateTime>> GetAllFiles(string path)
    {
        List<ValueTuple<string, long, DateTime>> fileList = new List<ValueTuple<string, long, DateTime>>();

        if (!Directory.Exists(path))
            return fileList;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (File.Exists(entry))
                {
                    FileInfo fileInfo = new FileInfo(entry);
                    fileList.Add((entry, fileInfo.Length, fileInfo.LastWriteTime));
                }
                else if (Directory.Exists(entry))
                {
                    fileList.AddRange(GetAllFiles(entry)); // 递归子目录
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error($"Access denied to: {path} - {ex.Message}");
        }

        return fileList;
    }

    /// <summary>
    /// 忽略系统文件, 不忽略隐藏
    /// 筛选特定后缀（如 .txt, .jpg）
    /// 如果 allowedExtensions==null, 则 获取所有类型文件
    /// </summary>
    /// <param name="path"></param>
    /// <param name="fileList"></param>
    /// <param name="allowedExtensions"></param>
    /// <returns></returns>
    public static List<string> GetAllFiles(
        string path,
        List<string>? fileList = null,
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
                    continue;

                if (File.Exists(entry))
                {
                    if (allowedExtensions.Length == 0 || HasAllowedExtension(entry, allowedExtensions))
                    {
                        fileList.Add(entry);
                    }
                }
                else if (Directory.Exists(entry))
                {
                    GetAllFiles(entry, fileList, allowedExtensions); // 递归
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error($"Access denied to: {path} - {ex.Message}");
        }

        return fileList;
    }

    /// <summary>
    /// 判断是否是 指定后缀格式的文件
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="allowedExtensions"></param>
    /// <returns></returns>
    public static bool HasAllowedExtension(string filePath, string[] allowedExtensions)
    {
        allowedExtensions ??= Array.Empty<string>();
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        foreach (var allowed in allowedExtensions)
        {
            if (ext == allowed.ToLowerInvariant())
                return true;
        }

        return false;
    }

    /// <summary>
    /// 是否没有扩展名
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static bool IsEmptyExtension(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext?.Length == 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取文件夹下所有文件类型(后缀), 忽略系统文件(attr.HasFlag(FileAttributes.Hidden))
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static List<string> GetAllFileExtensions(string path)
    {
        // Log.Debug("11");  哪个程序调用, 出现在哪个程序用
        var extensions = new HashSet<string>(); // 使用 HashSet 去重

        if (!Directory.Exists(path))
            return new List<string>(); // 如果目录不存在，返回空列表

        try
        {
            // 遍历目录下所有文件
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attr = File.GetAttributes(entry);

                // 如果是系统文件，跳过
                if (attr.HasFlag(FileAttributes.System))
                    continue;

                if (File.Exists(entry))
                {
                    // 获取文件的扩展名
                    string extension = Path.GetExtension(entry).ToLowerInvariant();
                    if (!string.IsNullOrEmpty(extension)) // 如果有扩展名，加入列表
                    {
                        extensions.Add(extension);
                    }
                }
                else if (Directory.Exists(entry))
                {
                    // 递归遍历子目录
                    var subDirectoryExtensions = GetAllFileExtensions(entry);
                    foreach (var ext in subDirectoryExtensions)
                    {
                        extensions.Add(ext);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error($"Access denied to: {path} - {ex.Message}");
        }

        return new List<string>(extensions); // 返回一个不重复的扩展名列表
    }
}