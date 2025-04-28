using System.IO;
using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;
using Serilog;

namespace PracticalTools.util;

public class FileUtil
{
    public ValueTuple<string, string, string> FileValueTuple { get; set; }

    public static string[] _skipFolders = new[]
    {
        "$Recycle.Bin",
        "System Volume Information",
        "Documents and Settings",
        "Recovery",
        "Config.Msi",
        "PerfLogs"
    };

    public static string[]? _defaultAllowedExtensions = { };


    public static string[]? _phototExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".svg", ".psd", ".raw"
    };

    public static string[]? _audioExtensions =
    {
        ".mp3", ".wav", ".flac", ".aac", ".wma", ".ogg", ".aiff", ".au", ".m4a", ".amr"
    };

    public static string[]? _videoExtensions =
    {
        ".mp4", ".avi", ".wmv", ".mkv", ".flv", ".mov", ".rm", ".rmvb", "m4v", ".mts", "m2ts", ".3gp", ".webm"
    };

    public static string[]? _wordExtensions =
    {
        ".txt", ".doc", ".docx", ".rtf", ".md", ".otd", ".pdf", ".ppt", ".pptx", ".xls", ".xlsx", ".csv", ".xml",
        ".xaml", ".json", ".html", ".ts",
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt", ".ttml", ".sami", ".rt"
    };

    // 压缩文件
    public static string[]? _compressedFileExtensions =
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab", ".iso", ".apk", ".jar", ".rpm"
    };

    public static string[]? _programExtensions =
    {
        ".c", ".cpp", ".java", ".py", ".js", ".ts", ".php", ".rb", ".go", ".swift", ".kt", ".cs", ".vb",
        ".scala", ".clj", ".erl", ".hs", ".lua", ".pl", ".tcl", ".vue",
        ".html", ".xhtml", ".css", ".scss", ".sass", ".less", "jsx", ".pug", ".hbs"
    };

    public static string[]? _nullExtensions = { };


    // 初始化日志（仅在 FileUtil 类中使用）
    static FileUtil()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console() // 控制台输出
            .WriteTo.File("logs/fileutil-log-.txt", rollingInterval: RollingInterval.Day) // 文件日志，按天归档
            .CreateLogger();

        Log.Information("FileUtil 初始化");
        _defaultAllowedExtensions = _phototExtensions.Concat(_phototExtensions)
            .Concat(_audioExtensions)
            .Concat(_videoExtensions)
            .Concat(_wordExtensions)
            .Concat(_compressedFileExtensions)
            .Concat(_programExtensions)
            .ToArray();
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
            Log.Information($"Progress: {progress:F2}%");
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
            Log.Error($"拒绝访问: {path} - {ex.Message}");
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
    //         Log.Error($"拒绝访问: {path} - {ex.Message}");
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
                if (_skipFolders.Any(f => entry.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    continue;

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
            Log.Error($"拒绝访问: {path} - {ex.Message}");
        }

        return fileList;
    }

    /// <summary>
    /// 可停止
    /// </summary>
    /// <param name="path"></param>
    /// <param name="fileList"></param>
    /// <returns></returns>
    public static List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)> GetAllFiles(
        string path,
        List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)>? fileList = null,
        CancellationToken token = default)
    {
        fileList ??= new List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)>();

        if (!Directory.Exists(path))
            return fileList;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                token.ThrowIfCancellationRequested(); // 支持取消

                if (_skipFolders.Any(f => entry.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (File.Exists(entry))
                {
                    FileInfo fileInfo = new FileInfo(entry);

                    fileList.Add((Path.GetFileName(entry), entry, fileInfo.Length, fileInfo.CreationTime, fileInfo.LastWriteTime));
                }
                else if (Directory.Exists(entry))
                {
                    GetAllFiles(entry, fileList, token); // 递归子目录
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error($"拒绝访问: {path} - {ex.Message}");
        }

        return fileList;
    }

    /// <summary>
    /// 扫描并计算 hash值
    /// skipFolders 跳过一些系统文件夹
    /// </summary>
    /// <param name="path"></param>
    /// <param name="fileList"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public static List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed, string Md5)> GetAllFiles(
        string path,
        List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed, string Md5)>? fileList = null,
        CancellationToken token = default,
        string[]? allowedExtensions = null)
    {
        allowedExtensions ??= _defaultAllowedExtensions;
        fileList ??= new List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed, string Md5)>();

        if (!Directory.Exists(path))
            return fileList;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                token.ThrowIfCancellationRequested(); // 支持取消

                if (_skipFolders.Any(f => entry.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (File.Exists(entry))
                {
                    if (allowedExtensions.Length == 0)
                    {
                        // 只扫描没有后缀的
                        if (IsEmptyExtension(entry))
                        {
                            byte[] md5 = ComputeFileHashMD5(entry);
                            string md5String = BitConverter.ToString(md5).Replace("-", "");

                            FileInfo fileInfo = new FileInfo(entry);

                            fileList.Add((Path.GetFileName(entry), entry, fileInfo.Length, fileInfo.CreationTime, fileInfo.LastWriteTime, md5String));
                        }
                    }
                    else
                    {
                        // 没有 后缀的 跳过
                        if (IsEmptyExtension(entry))
                        {
                            continue;
                        }

                        if (HasAllowedExtension(entry, allowedExtensions))
                        {
                            byte[] md5 = ComputeFileHashMD5(entry);
                            string md5String = BitConverter.ToString(md5).Replace("-", "");

                            FileInfo fileInfo = new FileInfo(entry);

                            fileList.Add((Path.GetFileName(entry), entry, fileInfo.Length, fileInfo.CreationTime, fileInfo.LastWriteTime, md5String));
                        }
                    }
                }
                else if (Directory.Exists(entry))
                {
                    GetAllFiles(entry, fileList, token, allowedExtensions); // 递归子目录
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error($"拒绝访问: {path} - {ex.Message}");
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
            Log.Error($"拒绝访问: {path} - {ex.Message}");
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
            Log.Error($"拒绝访问: {path} - {ex.Message}");
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
            Log.Error($"拒绝访问: {path} - {ex.Message}");
        }

        return new List<string>(extensions); // 返回一个不重复的扩展名列表
    }


    /// <summary>
    /// 删除文件
    /// </summary>
    /// <param name="filePath">filePath</param>
    /// <returns>void</returns>
    /// <author>wg</author>
    /// <date>2025年4月28日</date>
    public static bool DeleteFile(string filePath)
    {
        try
        {
            // 检查文件是否存在
            if (!File.Exists(filePath))
            {
                return true;
            }

            // 尝试删除文件
            File.Delete(filePath);
            Log.Information($"文件 {filePath} 已成功删除。");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            // 处理没有权限访问文件的异常
            Log.Error($"删除文件 {filePath} 时没有足够的权限：{ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            // 处理文件被占用或其他 I/O 异常
            Log.Error($"删除文件 {filePath} 时发生 I/O 错误：{ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            // 处理其他未知异常
            Log.Error($"删除文件 {filePath} 时发生未知错误：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 放入回收站
    /// </summary>
    /// <param name="filePath">filePath</param>
    /// <returns>RETURN_DESC</returns>
    /// <author>wg</author>
    /// <date>2025年4月28日</date>
    public static bool MoveFileToRecycleBin(string filePath)
    {
        try
        {
            // 将文件移至回收站
            FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            Log.Information($"文件 {filePath} 已成功移至回收站。");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"错误: {ex.Message}");
            return false;
        }
    }
}