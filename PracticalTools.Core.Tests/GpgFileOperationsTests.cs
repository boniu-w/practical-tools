using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.GPG;

namespace PracticalTools.Core.Tests;

[TestClass]
public class GpgFileOperationsTests
{
    private string _directory = null!;
    private static string _public = null!;
    private static string _secret = null!;

    [ClassInitialize]
    public static void GenerateKeys(TestContext context)
    {
        var pair = GpgHelper.GeneratePgpKeyPair("migration-test <test@example.com>", "test-password", 2048);
        _public = GpgHelper.ExportPublicKey(pair.pubRing);
        _secret = GpgHelper.ExportPrivateKey(pair.secRing);
    }

    [TestInitialize]
    public void Setup() => _directory = Directory.CreateTempSubdirectory("practical-tools-test-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public async Task EncryptDecryptRestoresExactBytes()
    {
        var input = Path.Combine(_directory, "中文 input.bin");
        var encrypted = Path.Combine(_directory, "encrypted.gpg");
        var output = Path.Combine(_directory, "restored.bin");
        var bytes = new byte[150_000];
        new Random(42).NextBytes(bytes);
        await File.WriteAllBytesAsync(input, bytes);
        await GpgFileOperations.EncryptAsync(input, encrypted, _public);
        await GpgFileOperations.DecryptAsync(encrypted, output, _secret, "test-password");
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(output));
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
    }

    [TestMethod]
    public async Task ExistingOutputIsNeverOverwritten()
    {
        var input = Path.Combine(_directory, "input");
        var output = Path.Combine(_directory, "output");
        await File.WriteAllTextAsync(input, "original");
        await File.WriteAllTextAsync(output, "keep me");
        await Assert.ThrowsExceptionAsync<IOException>(() => GpgFileOperations.EncryptAsync(input, output, _public));
        Assert.AreEqual("keep me", await File.ReadAllTextAsync(output));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => GpgFileOperations.EncryptAsync(input, input, _public));
        Assert.AreEqual("original", await File.ReadAllTextAsync(input));
    }

    [TestMethod]
    public async Task WrongPassphrasePublishesNoOutputAndCleansTemporaryFiles()
    {
        var input = Path.Combine(_directory, "input");
        var encrypted = Path.Combine(_directory, "encrypted.gpg");
        var output = Path.Combine(_directory, "output");
        await File.WriteAllTextAsync(input, "secret content");
        await GpgFileOperations.EncryptAsync(input, encrypted, _public);
        Exception? failure = null;
        try { await GpgFileOperations.DecryptAsync(encrypted, output, _secret, "wrong password"); }
        catch (Exception ex) { failure = ex; }
        Assert.IsNotNull(failure);
        Assert.IsFalse(File.Exists(output));
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
    }
}
