using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using PracticalTools.Storage;

namespace PracticalTools.GPG.Folders;

public sealed class FolderCryptoService(FolderCryptoLimits? limits = null) : IFolderCryptoService
{
    private readonly FolderCryptoLimits _limits = limits ?? new();
    private sealed record SourceFile(string Path, string Relative, long Length, DateTime Created, DateTime Written);
    private sealed record SourceDirectory(string Path, string Relative, DateTime Written);
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private sealed class ThrottledProgress(IProgress<FolderCryptoProgress>? target) : IProgress<FolderCryptoProgress>
    {
        private string? _phase;
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        public void Report(FolderCryptoProgress value)
        {
            if (_phase != value.Phase || _timer.ElapsedMilliseconds >= 100 || (value.Total > 0 && value.Completed == value.Total))
            { _phase = value.Phase; _timer.Restart(); target?.Report(value); }
        }
    }

    public Task<FolderCryptoResult> EncryptAsync(string source, string outputFile, string publicKey,
        IProgress<FolderCryptoProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Encrypt(source, outputFile, publicKey, new ThrottledProgress(progress), cancellationToken), cancellationToken);

    public Task<FolderCryptoResult> DecryptAsync(string inputFile, string outputDirectory, string secretKey, string passphrase,
        IProgress<FolderCryptoProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Decrypt(inputFile, outputDirectory, secretKey, passphrase, new ThrottledProgress(progress), cancellationToken), cancellationToken);

    private static bool Within(string parent, string path)
    {
        var root = LocalFileAccess.Identity(parent);
        var child = LocalFileAccess.Identity(path);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return child == root || child.StartsWith(prefix, StringComparison.Ordinal);
    }

    private string PrepareOutput(string path)
    {
        if (_limits.MaxEntries < 1 || _limits.MaxPlainBytes < 1 || _limits.MaxZipBytes < 1) throw new ArgumentException("归档限制无效。");
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("输出已存在，请选择新的文件或目录名称。");
        var parent = Path.GetDirectoryName(path) ?? throw new ArgumentException("请选择完整输出路径。");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("输出的父目录不存在。");
        LocalFileAccess.NoLinks(parent);
        return parent;
    }

    private FolderCryptoResult Encrypt(string source, string output, string publicKey, IProgress<FolderCryptoProgress>? progress, CancellationToken token)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)); output = Path.GetFullPath(output);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("输入目录不存在。");
        LocalFileAccess.NoLinks(source);
        var parent = PrepareOutput(output);
        if (Within(source, output)) throw new ArgumentException("加密输出不能位于输入目录内。");
        progress?.Report(new("枚举目录", source, 0, 0, 0, 0));
        var files = new List<SourceFile>(); var directories = new List<SourceDirectory>();
        var pending = new Stack<string>(); pending.Push(source);
        var names = new ArchiveNames();
        long total = 0;
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            LocalFileAccess.NoLinks(directory);
            var relative = directory == source ? "" : Path.GetRelativePath(source, directory).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Length > 0) names.Add(relative, true);
            directories.Add(new(directory, relative, Directory.GetLastWriteTimeUtc(directory)));
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"输入含链接/重解析点，未打包：{path}");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else
                {
                    var info = new FileInfo(path);
                    var name = names.Add(Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/'), false);
                    if (info.Length > _limits.MaxPlainBytes - total) throw new InvalidDataException("输入目录大小超过归档限制。");
                    total += info.Length;
                    files.Add(new(path, name, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc));
                }
                if ((long)files.Count + directories.Count + pending.Count - 1 > _limits.MaxEntries)
                    throw new InvalidDataException("输入目录条目数超过限制。");
            }
        }
        var staged = Path.Combine(parent, $".practical-tools-cipher-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var workspace = new PrivateWorkspace(parent))
            {
                var zipPath = Path.Combine(workspace.Root, "payload.zip");
                using (var stream = PrivateWorkspace.CreateFile(zipPath))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
                {
                    foreach (var directory in directories.Where(d => d.Relative.Length > 0)) zip.CreateEntry(directory.Relative + "/");
                    long bytes = 0; var completed = 0;
                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        LocalFileAccess.CheckSnapshot(file.Path, source, file.Length, file.Created, file.Written);
                        using var input = LocalFileAccess.Open(file.Path);
                        if (!input.CanSeek) throw new IOException($"输入不是普通文件：{file.Path}");
                        var entry = zip.CreateEntry(file.Relative, CompressionLevel.Optimal);
                        using (var target = entry.Open())
                            CopyExact(input, target, file.Length, token, copied => progress?.Report(new("打包", file.Relative, completed, files.Count, bytes + copied, total)));
                        LocalFileAccess.CheckSnapshot(file.Path, source, file.Length, file.Created, file.Written);
                        bytes += file.Length; completed++;
                        progress?.Report(new("打包", file.Relative, completed, files.Count, bytes, total));
                    }
                }
                foreach (var directory in directories)
                {
                    LocalFileAccess.NoLinks(directory.Path);
                    if (Directory.GetLastWriteTimeUtc(directory.Path) != directory.Written) throw new IOException("输入目录在打包期间发生变化，请重试。");
                }
                var zipSize = new FileInfo(zipPath).Length;
                if (zipSize > _limits.MaxZipBytes) throw new InvalidDataException("ZIP 大小超过归档限制。");
                progress?.Report(new("PGP 加密", "", 0, 1, 0, zipSize));
                token.ThrowIfCancellationRequested();
                var cipher = Path.Combine(workspace.Root, "encrypted.gpg");
                GpgHelper.EncryptFileForArchive(zipPath, cipher, publicKey, token,
                    new InlineProgress<long>(bytes => progress?.Report(new("PGP 加密", "", bytes == zipSize ? 1 : 0, 1, bytes, zipSize))));
                File.Delete(zipPath);
                File.Move(cipher, staged, overwrite: false);
            }
            progress?.Report(new("发布结果", output, files.Count, files.Count, total, total));
            token.ThrowIfCancellationRequested(); PrepareOutput(output);
            File.Move(staged, output, overwrite: false);
            return new(output, files.Count, directories.Count, total);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    private FolderCryptoResult Decrypt(string input, string output, string secretKey, string passphrase,
        IProgress<FolderCryptoProgress>? progress, CancellationToken token)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        if (!File.Exists(input)) throw new FileNotFoundException("输入归档不存在。", input);
        LocalFileAccess.NoLinks(input);
        var parent = PrepareOutput(output);
        using var extracted = new PrivateWorkspace(parent);
        var staged = extracted.Root;
        var files = 0; var directories = 1; long total = 0;
        {
            using (var workspace = new PrivateWorkspace(parent))
            {
                var zipPath = Path.Combine(workspace.Root, "payload.zip");
                progress?.Report(new("PGP 解密及完整性校验", "", 0, 0, 0, 0));
                GpgHelper.DecryptFileForArchive(input, zipPath, secretKey, passphrase, token,
                    new InlineProgress<long>(bytes => progress?.Report(new("PGP 解密及完整性校验", "", 0, 0, bytes, 0))), _limits.MaxZipBytes);
                var zipBytes = new FileInfo(zipPath).Length;
                progress?.Report(new("PGP 解密及完整性校验", "", 1, 1, zipBytes, zipBytes));
                token.ThrowIfCancellationRequested();
                progress?.Report(new("检查 ZIP", "", 0, 0, 0, 0));
                using (var stream = File.OpenRead(zipPath))
                {
                    ZipDirectoryPreflight.Validate(stream, _limits.MaxEntries, token);
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
                    var registry = new ArchiveNames();
                    var plan = new List<(ZipArchiveEntry Entry, string Relative, bool Directory)>();
                    foreach (var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        var directory = entry.FullName.EndsWith('/');
                        var type = ((uint)entry.ExternalAttributes >> 16) & 0xf000;
                        if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                            (type != 0 && type != (directory ? 0x4000u : 0x8000u)) || (directory && entry.Length != 0))
                            throw new InvalidDataException("ZIP 包含链接、特殊文件或不一致的目录条目。");
                        var name = registry.Add(entry.FullName, directory);
                        if (entry.Length > _limits.MaxPlainBytes - total) throw new InvalidDataException("解压数据大小超过限制。");
                        total += entry.Length;
                        plan.Add((entry, name, directory));
                    }
                    var tree = staged;
                    directories = registry.DirectoryCount + 1;
                    long bytes = 0; var completed = 0;
                    foreach (var item in plan)
                    {
                        token.ThrowIfCancellationRequested();
                        var target = Path.GetFullPath(Path.Combine(tree, item.Relative.Replace('/', Path.DirectorySeparatorChar)));
                        if (!Within(tree, target)) throw new InvalidDataException("解压路径超出目标目录。");
                        if (item.Directory) PrivateWorkspace.CreateDirectory(target);
                        else
                        {
                            var folder = Path.GetDirectoryName(target)!;
                            PrivateWorkspace.CreateDirectory(folder); LocalFileAccess.NoLinks(folder);
                            using var source = item.Entry.Open();
                            using var destination = PrivateWorkspace.CreateFile(target);
                            CopyExact(source, destination, item.Entry.Length, token,
                                copied => progress?.Report(new("解压", item.Relative, completed, plan.Count, bytes + copied, total)));
                            bytes += item.Entry.Length; files++;
                        }
                        completed++;
                        progress?.Report(new("解压", item.Relative, completed, plan.Count, bytes, total));
                    }
                }
                File.Delete(zipPath);
            }
            progress?.Report(new("发布结果", output, files, files, total, total));
            token.ThrowIfCancellationRequested(); PrepareOutput(output);
            Directory.Move(staged, output);
            return new(output, files, directories, total);
        }
    }

    private static void CopyExact(Stream source, Stream target, long expected, CancellationToken token, Action<long> progress)
    {
        var buffer = new byte[65536]; long copied = 0;
        var timer = Stopwatch.StartNew();
        while (copied < expected)
        {
            token.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, expected - copied));
            if (read == 0) throw new InvalidDataException("文件/归档内容长度与声明不一致。");
            target.Write(buffer, 0, read); copied += read;
            if (timer.ElapsedMilliseconds >= 100) { progress(copied); timer.Restart(); }
        }
        token.ThrowIfCancellationRequested();
        if (source.ReadByte() != -1) throw new InvalidDataException("文件/归档内容长度与声明不一致。");
        progress(copied);
    }
}
