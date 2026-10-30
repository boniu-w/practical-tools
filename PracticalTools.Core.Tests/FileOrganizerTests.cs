using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.FileManager;

namespace PracticalTools.Core.Tests;

[TestClass]
public class FileOrganizerTests
{
    private string _root = null!;
    [TestInitialize] public void Setup() => _root = Directory.CreateTempSubdirectory("pt-organize-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);
    [TestMethod]
    public async Task PreviewIsReadOnlyAndMovePreservesCollisionsAndNestedFiles()
    {
        File.WriteAllText(Path.Combine(_root, "a.TXT"), "a"); File.WriteAllText(Path.Combine(_root, "b.txt"), "b");
        Directory.CreateDirectory(Path.Combine(_root, "txt")); File.WriteAllText(Path.Combine(_root, "txt", "b.txt"), "original");
        var service = new FileOrganizer(); var plan = await service.PreviewAsync(_root, ".txt,TXT");
        Assert.AreEqual(2, plan.Items.Count); Assert.IsTrue(File.Exists(Path.Combine(_root, "a.TXT")));
        var result = await service.ApplyAsync(plan); Assert.AreEqual(1, result.Moved); Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(_root, "txt", "b.txt")));
        Assert.AreEqual("b", File.ReadAllText(Path.Combine(_root, "b.txt")));
    }
    [TestMethod]
    public async Task ChangedContentWithRestoredTimestampIsRejected()
    {
        var path = Path.Combine(_root, "a.txt"); File.WriteAllText(path, "old");
        var service = new FileOrganizer(); var plan = await service.PreviewAsync(_root, "txt");
        File.WriteAllText(path, "new"); File.SetLastWriteTimeUtc(path, plan.Items[0].Modified);
        var result = await service.ApplyAsync(plan); Assert.AreEqual(0, result.Moved); Assert.AreEqual(1, result.Errors.Count);
    }
    [TestMethod]
    public async Task CancellationAndForgedDestinationNeverMoveFiles()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "original");
        var service = new FileOrganizer(); var plan = await service.PreviewAsync(_root, "txt");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancelled = await service.ApplyAsync(plan, cts.Token); Assert.IsTrue(cancelled.Cancelled); Assert.AreEqual(0, cancelled.Moved);
        var forged = plan with { Items = [plan.Items[0] with { Target = Path.Combine(_root, "other.txt") }] };
        var result = await service.ApplyAsync(forged); Assert.AreEqual(0, result.Moved); Assert.AreEqual(1, result.Errors.Count);
    }
    [DataTestMethod]
    [DataRow("../txt")]
    [DataRow("")]
    [DataRow("a/b")]
    public async Task UnsafeExtensionsAreRejected(string suffix) =>
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => new FileOrganizer().PreviewAsync(_root, suffix));
}
