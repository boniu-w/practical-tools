using System.Diagnostics;
using System.Security.Cryptography;

namespace PracticalTools.DuplicateFiles.Scanning;

public sealed class DuplicateScanner(IFileRemoval? removal = null) : IDuplicateScanner
{
    private readonly IFileRemoval _removal = removal ?? new PlatformFileRemoval();
    public bool CanRecycle => _removal.CanRecycle;

    public Task<DuplicateScan> ScanAsync(IReadOnlyList<string> roots, string extensions,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(roots, extensions, progress, cancellationToken), cancellationToken);

    private static DuplicateScan Scan(IReadOnlyList<string> inputRoots, string extensions,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var roots = inputRoots.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Trim())))
            .DistinctBy(ScanFileAccess.Identity).ToList();
        if (roots.Count == 0) throw new ArgumentException("请选择至少一个目录。");
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"目录不存在：{root}");
            ScanFileAccess.NoLinks(root);
        }
        var filter = ParseExtensions(extensions);
        var files = new Dictionary<string, ScannedFile>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(string Path, string Root)>(roots.Select(r => (r, r)));
        var warnings = new List<ScanWarning>();
        var skipped = 0;
        void Warn(string path, string reason) { skipped++; if (warnings.Count < 200) warnings.Add(new(path, reason)); }
        var throttle = Stopwatch.StartNew();
        progress?.Report(new("枚举目录", 0, 0, 0, 0, 0));
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(ScanFileAccess.Identity(directory.Path))) continue;
            try
            {
                ScanFileAccess.NoLinks(directory.Path);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { Warn(path, "符号链接/重解析点，已跳过。"); continue; }
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push((path, directory.Root)); continue; }
                        if (filter.Count > 0 && !filter.Contains(Path.GetExtension(path))) continue;
                        var info = new FileInfo(path);
                        var identity = ScanFileAccess.Identity(path);
                        if (files.TryGetValue(identity, out var previous) && previous.FilePath != path)
                            Warn(path, "路径仅大小写或 Unicode 形式不同，已保守跳过，避免别名被当作两份文件。");
                        else files.TryAdd(identity, new(path, directory.Root, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc));
                        if (files.Count > 100000) throw new InvalidOperationException("单次扫描最多 100000 个文件，请缩小目录或扩展名范围。");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn(path, ex.Message); }
                    if (throttle.ElapsedMilliseconds >= 100)
                    {
                        progress?.Report(new("枚举目录", files.Count, 0, 0, 0, 0));
                        throttle.Restart();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn(directory.Path, ex.Message); }
        }
        var candidates = files.Values.GroupBy(f => f.Length).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
        long bytesRead = 0, totalBytes = candidates.Sum(f => f.Length);
        var hashed = 0;
        var processed = 0;
        progress?.Report(new("比较内容", files.Count, hashed, candidates.Count, bytesRead, totalBytes));
        foreach (var file in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ScanFileAccess.CheckSnapshot(file);
                using var stream = ScanFileAccess.Open(file.FilePath);
                if (!stream.CanSeek) throw new IOException("不是可定位的普通文件，已跳过。");
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                var buffer = new byte[65536];
                long remaining = file.Length;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) throw new IOException("读取期间文件长度变化，已跳过。");
                    remaining -= read; bytesRead += read;
                    sha.AppendData(buffer, 0, read); md5.AppendData(buffer, 0, read);
                    if (throttle.ElapsedMilliseconds >= 100)
                    {
                        progress?.Report(new("比较内容", files.Count, hashed, candidates.Count, bytesRead, totalBytes));
                        throttle.Restart();
                    }
                }
                if (stream.ReadByte() != -1) throw new IOException("读取期间文件长度变化，已跳过。");
                ScanFileAccess.CheckSnapshot(file);
                files[ScanFileAccess.Identity(file.FilePath)] = file with
                { Sha256 = Convert.ToHexString(sha.GetHashAndReset()), Md5 = Convert.ToHexString(md5.GetHashAndReset()) };
                hashed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn(file.FilePath, ex.Message); }
            processed++;
            if (throttle.ElapsedMilliseconds >= 100 || processed == candidates.Count)
            {
                progress?.Report(new("比较内容", files.Count, hashed, candidates.Count, bytesRead, totalBytes));
                throttle.Restart();
            }
        }
        token.ThrowIfCancellationRequested();
        return new(roots, files.Values.ToList(), warnings, skipped, DateTime.UtcNow);
    }

    private static HashSet<string> ParseExtensions(string text)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split([';', ',', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = raw.StartsWith("*.", StringComparison.Ordinal) ? raw[1..] : raw.StartsWith('.') ? raw : "." + raw;
            if (value.Length < 2 || value.IndexOfAny(['*', '?', '/', '\\', ':']) >= 0) throw new ArgumentException("扩展名格式无效，请使用 .txt;.pdf，留空为全部。");
            extensions.Add(value);
        }
        return extensions;
    }

    public Task<FileRemovalReport> RemoveAsync(DuplicateScan scan, IReadOnlyList<ScannedFile> selected, FileRemovalMode mode) =>
        Task.Run(() => Remove(scan, selected, mode));

    private FileRemovalReport Remove(DuplicateScan scan, IReadOnlyList<ScannedFile> selected, FileRemovalMode mode)
    {
        if (mode == FileRemovalMode.Recycle && !CanRecycle) throw new PlatformNotSupportedException("当前平台尚未接入回收站，不会自动降级为永久删除。");
        var requested = selected.DistinctBy(f => ScanFileAccess.Identity(f.FilePath)).ToList();
        if (requested.Count == 0) throw new ArgumentException("请先选择要移除的文件。");
        if (requested.Any(f => !scan.Duplicates.Contains(f))) throw new ArgumentException("所选文件不属于本次完整扫描的重复结果。");
        var paths = requested.Select(f => f.FilePath).ToHashSet(StringComparer.Ordinal);
        var groups = scan.Duplicates.GroupBy(f => (f.Length, f.Sha256)).Where(g => g.Any(f => paths.Contains(f.FilePath))).ToList();
        if (groups.Any(g => g.All(f => paths.Contains(f.FilePath))))
            throw new InvalidOperationException("每组必须至少保留一份文件。请取消选择一份，或使用智能选择。");
        var outcomes = new List<FileRemovalOutcome>();
        var cancelled = false;
        foreach (var group in groups)
        {
            if (cancelled) break;
            FileStream? keeper = null;
            ScannedFile? keeperFile = null;
            try
            {
                foreach (var file in group.Where(f => !paths.Contains(f.FilePath)))
                {
                    try { keeper = OpenVerified(file, false); keeperFile = file; break; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                if (keeper == null) throw new IOException("保留文件已变化、丢失或无法读取，请重新扫描。");
                foreach (var file in group.Where(f => paths.Contains(f.FilePath)))
                {
                    try
                    {
                        using var candidate = OpenVerified(file, true);
                        ScanFileAccess.CheckSnapshot(keeperFile!);
                        keeper.Position = candidate.Position = 0;
                        if (!EqualBytes(keeper, candidate, file.Length)) throw new IOException("文件内容已不同，请重新扫描。");
                        ScanFileAccess.CheckSnapshot(file);
                        ScanFileAccess.CheckSnapshot(keeperFile!);
                        _removal.Remove(file.FilePath, mode);
                        if (File.Exists(file.FilePath)) throw new IOException("系统未移除该文件。");
                        outcomes.Add(new(file.FilePath, true, null));
                    }
                    catch (OperationCanceledException) { cancelled = true; break; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { outcomes.Add(new(file.FilePath, false, ex.Message)); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes.AddRange(group.Where(f => paths.Contains(f.FilePath)).Select(f => new FileRemovalOutcome(f.FilePath, false, ex.Message)));
            }
            finally { keeper?.Dispose(); }
        }
        var attempted = outcomes.Select(o => o.Path).ToHashSet(StringComparer.Ordinal);
        outcomes.AddRange(requested.Where(f => !attempted.Contains(f.FilePath)).Select(f => new FileRemovalOutcome(f.FilePath, false, "用户取消，未处理。")));
        return new(outcomes, cancelled);
    }

    private static FileStream OpenVerified(ScannedFile file, bool allowDelete)
    {
        ScanFileAccess.CheckSnapshot(file);
        var stream = ScanFileAccess.Open(file.FilePath, allowDelete);
        try
        {
            if (!stream.CanSeek || stream.Length != file.Length)
                throw new IOException("文件内容在扫描后已变化，请重新扫描。");
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            for (long remaining = file.Length; remaining > 0;)
            {
                var count = (int)Math.Min(remaining, buffer.Length);
                stream.ReadExactly(buffer, 0, count);
                sha.AppendData(buffer, 0, count);
                remaining -= count;
            }
            if (stream.ReadByte() != -1 || Convert.ToHexString(sha.GetHashAndReset()) != file.Sha256)
                throw new IOException("文件内容在扫描后已变化，请重新扫描。");
            ScanFileAccess.CheckSnapshot(file);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static bool EqualBytes(Stream left, Stream right, long length)
    {
        var a = new byte[65536]; var b = new byte[65536];
        while (length > 0)
        {
            var count = (int)Math.Min(length, a.Length);
            left.ReadExactly(a, 0, count); right.ReadExactly(b, 0, count);
            if (!a.AsSpan(0, count).SequenceEqual(b.AsSpan(0, count))) return false;
            length -= count;
        }
        return left.ReadByte() == -1 && right.ReadByte() == -1;
    }
}
