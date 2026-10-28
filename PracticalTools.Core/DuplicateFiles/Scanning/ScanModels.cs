namespace PracticalTools.DuplicateFiles.Scanning;

public sealed record ScannedFile(string FilePath, string Root, long Length, DateTime CreationUtc,
    DateTime LastWriteUtc, string? Sha256 = null, string? Md5 = null)
{
    public string Name => Path.GetFileName(FilePath);
    public string Group => Sha256 == null ? "" : Sha256[..12];
}

public sealed record ScanWarning(string Path, string Reason);
public sealed record ScanProgress(string Phase, int Discovered, int Hashed, int Candidates, long BytesRead, long TotalBytes);

public sealed class DuplicateScan(IReadOnlyList<string> roots, IReadOnlyList<ScannedFile> files,
    IReadOnlyList<ScanWarning> warnings, int skipped, DateTime completedUtc)
{
    public IReadOnlyList<string> Roots { get; } = roots;
    public IReadOnlyList<ScannedFile> Files { get; } = files;
    public IReadOnlyList<ScanWarning> Warnings { get; } = warnings;
    public int Skipped { get; } = skipped;
    public DateTime CompletedUtc { get; } = completedUtc;
    public IReadOnlyList<ScannedFile> Duplicates { get; } = files.Where(f => f.Sha256 != null)
        .GroupBy(f => (f.Length, f.Sha256)).Where(g => g.Count() > 1)
        .OrderBy(g => g.Key.Sha256, StringComparer.Ordinal).SelectMany(g => g.OrderBy(f => f.FilePath, StringComparer.Ordinal)).ToList();
    public decimal DuplicateBytes => Duplicates.GroupBy(f => (f.Length, f.Sha256)).Sum(g => (decimal)(g.Count() - 1) * g.Key.Length);

    public static List<ScannedFile> SuggestRemovals(IEnumerable<ScannedFile> files) => files.Where(f => f.Sha256 != null)
        .GroupBy(f => (f.Length, f.Sha256)).SelectMany(g => g.OrderBy(f => f.CreationUtc)
            .ThenBy(f => f.FilePath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ThenBy(f => f.FilePath.Length).ThenBy(f => f.FilePath, StringComparer.Ordinal).Skip(1)).ToList();

    public DuplicateScan Without(IEnumerable<string> removed)
    {
        var paths = removed.ToHashSet(StringComparer.Ordinal);
        return new(Roots, Files.Where(f => !paths.Contains(f.FilePath)).ToList(), Warnings, Skipped, CompletedUtc);
    }
}

public enum FileRemovalMode { Recycle, Permanent }
public sealed record FileRemovalOutcome(string Path, bool Success, string? Error);
public sealed record FileRemovalReport(IReadOnlyList<FileRemovalOutcome> Outcomes, bool Cancelled);

public interface IDuplicateScanner
{
    bool CanRecycle { get; }
    Task<DuplicateScan> ScanAsync(IReadOnlyList<string> roots, string extensions,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<FileRemovalReport> RemoveAsync(DuplicateScan scan, IReadOnlyList<ScannedFile> selected, FileRemovalMode mode);
}

public interface IFileRemoval
{
    bool CanRecycle { get; }
    void Remove(string path, FileRemovalMode mode);
}
