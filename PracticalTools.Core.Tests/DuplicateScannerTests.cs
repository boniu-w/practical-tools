using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.DuplicateFiles.Scanning;
using PracticalTools.Sqlite;
using PracticalTools.Storage;

namespace PracticalTools.Core.Tests;

[TestClass]
public class DuplicateScannerTests
{
    private string _root = null!, _content = null!;
    private sealed class InlineProgress(Action<ScanProgress> action) : IProgress<ScanProgress>
    { public void Report(ScanProgress value) => action(value); }
    private sealed class TestRemoval(Action<string, FileRemovalMode>? action = null, bool canRecycle = false) : IFileRemoval
    {
        public bool CanRecycle => canRecycle;
        public void Remove(string path, FileRemovalMode mode) { if (action != null) action(path, mode); else File.Delete(path); }
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("pt-scan-tests-").FullName;
        _content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
    }
    [TestCleanup]
    public void Cleanup() { SqliteConnection.ClearAllPools(); Directory.Delete(_root, recursive: true); }
    private string Put(string name, string content)
    {
        var path = Path.Combine(_content, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [TestMethod]
    public async Task GroupsByContentsAcrossOverlappingRootsAndFiltersExtensions()
    {
        Put("one.txt", "alpha"); Put("nested/two.TXT", "alpha");
        Put("same-size-different.txt", "bravo"); Put("unique.bin", "uniquely sized content");
        var scanner = new DuplicateScanner();
        var scan = await scanner.ScanAsync([_content, Path.Combine(_content, "nested"), _content], "*.txt");
        Assert.AreEqual(3, scan.Files.Count);
        Assert.AreEqual(2, scan.Duplicates.Count);
        Assert.AreEqual(5m, scan.DuplicateBytes);
        Assert.IsTrue(scan.Duplicates.All(f => f.Sha256!.Length == 64 && f.Md5!.Length == 32));
        Assert.IsFalse(scan.Duplicates.Any(f => f.Name == "same-size-different.txt"));
    }

    [TestMethod]
    public async Task EmptyFilesAreDuplicatesAndUniqueSizesDoNotNeedHashing()
    {
        Put("empty-a", ""); Put("empty-b", ""); Put("unique", "unique content");
        var scan = await new DuplicateScanner().ScanAsync([_content], "");
        Assert.AreEqual(2, scan.Duplicates.Count);
        Assert.IsNull(scan.Files.Single(f => f.Name == "unique").Sha256);
        Assert.AreEqual(0m, scan.DuplicateBytes);
    }

    [TestMethod]
    public async Task InvalidInputsAndCancellationDoNotPublishPartialResults()
    {
        var scanner = new DuplicateScanner();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => scanner.ScanAsync([], ""));
        await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(() => scanner.ScanAsync([Path.Combine(_root, "missing")], ""));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => scanner.ScanAsync([_content], "../txt"));
        Put("a", "same"); Put("b", "same");
        using var cancel = new CancellationTokenSource();
        var cancelled = false;
        try
        {
            await scanner.ScanAsync([_content], "", new InlineProgress(p => { if (p.Phase == "比较内容") cancel.Cancel(); }), cancel.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        Assert.IsTrue(cancelled);
        Assert.AreEqual(2, Directory.GetFiles(_content).Length);
    }

    [TestMethod]
    public async Task SmartSelectionRetainsOneAndRemovalRevalidatesContent()
    {
        Put("a", "same"); Put("nested/b", "same"); Put("c", "same");
        var scanner = new DuplicateScanner(new TestRemoval());
        var scan = await scanner.ScanAsync([_content], "");
        var selected = DuplicateScan.SuggestRemovals(scan.Duplicates);
        Assert.AreEqual(2, selected.Count);
        var keep = scan.Duplicates.Except(selected).Single();
        var removed = await scanner.RemoveAsync(scan, selected, FileRemovalMode.Permanent);
        Assert.IsTrue(removed.Outcomes.All(o => o.Success));
        Assert.IsTrue(File.Exists(keep.FilePath));
        Assert.AreEqual("same", File.ReadAllText(keep.FilePath));
        Assert.AreEqual(0, scan.Without(removed.Outcomes.Select(o => o.Path)).Duplicates.Count);
    }

    [TestMethod]
    public async Task SelectingEveryCopyOrAnUnscannedFileIsRejectedBeforeRemoval()
    {
        Put("a", "same"); Put("b", "same");
        var scanner = new DuplicateScanner(new TestRemoval());
        var scan = await scanner.ScanAsync([_content], "");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => scanner.RemoveAsync(scan, scan.Duplicates, FileRemovalMode.Permanent));
        var forged = scan.Duplicates[0] with { FilePath = Path.Combine(_root, "outside") };
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => scanner.RemoveAsync(scan, [forged], FileRemovalMode.Permanent));
        Assert.AreEqual(2, Directory.GetFiles(_content).Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedContentEvenWithOriginalTimestampOrMissingKeeperPreventsRemoval(bool missingKeeper)
    {
        Put("a", "same"); Put("b", "same");
        var scanner = new DuplicateScanner(new TestRemoval());
        var scan = await scanner.ScanAsync([_content], "");
        var selected = scan.Duplicates[0];
        var keep = scan.Duplicates[1];
        if (missingKeeper) File.Delete(keep.FilePath);
        else
        {
            File.WriteAllText(selected.FilePath, "evil");
            File.SetLastWriteTimeUtc(selected.FilePath, selected.LastWriteUtc);
        }
        var report = await scanner.RemoveAsync(scan, [selected], FileRemovalMode.Permanent);
        Assert.IsFalse(report.Outcomes.Single().Success);
        Assert.IsTrue(File.Exists(selected.FilePath));
    }

    [TestMethod]
    public async Task PerFileFailuresKeepFailedRowsAndNativeCancellationStopsTheBatch()
    {
        Put("a", "same"); Put("b", "same"); Put("c", "same");
        var scan = await new DuplicateScanner().ScanAsync([_content], "");
        var failed = scan.Duplicates[1];
        var success = scan.Duplicates[2];
        var scanner = new DuplicateScanner(new TestRemoval((path, _) =>
        { if (path == failed.FilePath) throw new IOException("simulated failure"); File.Delete(path); }));
        var report = await scanner.RemoveAsync(scan, [failed, success], FileRemovalMode.Permanent);
        Assert.IsTrue(File.Exists(failed.FilePath));
        Assert.IsFalse(File.Exists(success.FilePath));
        Assert.AreEqual(1, report.Outcomes.Count(o => o.Success));
        Assert.AreEqual(2, scan.Without(report.Outcomes.Where(o => o.Success).Select(o => o.Path)).Duplicates.Count);
        await Assert.ThrowsExceptionAsync<PlatformNotSupportedException>(() =>
            scanner.RemoveAsync(scan, [failed], FileRemovalMode.Recycle));
        var cancelScanner = new DuplicateScanner(new TestRemoval((_, _) => throw new OperationCanceledException(), canRecycle: true));
        var cancelled = await cancelScanner.RemoveAsync(scan, [failed], FileRemovalMode.Recycle);
        Assert.IsTrue(cancelled.Cancelled);
        Assert.IsFalse(cancelled.Outcomes.Single().Success);
    }

    [TestMethod]
    public async Task LockedFileIsSkippedWithWarningInsteadOfBecomingDuplicate()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows sharing semantics test."); return; }
        var a = Put("a", "same"); Put("b", "same");
        using var locked = new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var scan = await new DuplicateScanner().ScanAsync([_content], "");
        Assert.AreEqual(0, scan.Duplicates.Count);
        Assert.IsTrue(scan.Skipped > 0);
    }

    [TestMethod]
    public async Task SymlinkTargetsAreSkippedAndLinkedRootsRejected()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "outside-file"), "same");
        Put("real", "same");
        var link = Path.Combine(_content, "link");
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (UnauthorizedAccessException) { Assert.Inconclusive("Creating a symlink requires platform privileges."); return; }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        { Assert.Inconclusive("Windows account has no symbolic-link creation privilege."); return; }
        try
        {
            var scanner = new DuplicateScanner();
            var scan = await scanner.ScanAsync([_content], "");
            Assert.AreEqual(1, scan.Files.Count);
            Assert.AreEqual(0, scan.Duplicates.Count);
            Assert.IsTrue(scan.Skipped > 0);
            await Assert.ThrowsExceptionAsync<IOException>(() => scanner.ScanAsync([link], ""));
            Assert.AreEqual("same", File.ReadAllText(Path.Combine(outside, "outside-file")));
        }
        finally { Directory.Delete(link); }
    }

    [TestMethod]
    public async Task CompletedScanHistoryPersistsWithLegacyMd5Semantics()
    {
        SQLitePCL.Batteries_V2.Init();
        Put("a", "same"); Put("b", "same"); Put("unique", "unique content");
        var scan = await new DuplicateScanner().ScanAsync([_content], "");
        var paths = new AppDataPaths(Path.Combine(_root, "data"));
        paths.EnsureDirectories();
        var services = new ServiceCollection();
        services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(paths.DatabasePath)));
        services.AddSingleton<ScanHistoryStore>();
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        {
            using (var scope = provider.CreateScope()) DatabaseInitializer.Initialize(scope.ServiceProvider.GetRequiredService<MyDbContext>());
            await provider.GetRequiredService<ScanHistoryStore>().SaveAsync(scan);
        }
        using var restarted = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var history = await restarted.GetRequiredService<ScanHistoryStore>().RecentAsync();
        Assert.AreEqual(3, history.Count);
        Assert.AreEqual(2, history.Count(f => f.Md5Value?.Length == 32));
        Assert.IsTrue(history.All(f => f.FileSize >= 0 && f.ScanDirectory == _content));
    }
}
