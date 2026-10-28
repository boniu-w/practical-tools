using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using PracticalTools.GPG;
using PracticalTools.GPG.Folders;

namespace PracticalTools.Core.Tests;

[TestClass]
public class FolderCryptoTests
{
    private string _root = null!, _source = null!;
    private static string _public = null!, _secret = null!;
    private sealed class InlineProgress(Action<FolderCryptoProgress> action) : IProgress<FolderCryptoProgress>
    { public void Report(FolderCryptoProgress value) => action(value); }

    [ClassInitialize]
    public static void Keys(TestContext context)
    {
        var pair = GpgHelper.GeneratePgpKeyPair("folder tests", "folder-password", 2048);
        _public = GpgHelper.ExportPublicKey(pair.pubRing); _secret = GpgHelper.ExportPrivateKey(pair.secRing);
    }
    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("pt-folder-tests-").FullName;
        _source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        Directory.CreateDirectory(Path.Combine(_source, "空目录"));
        Directory.CreateDirectory(Path.Combine(_source, "nested"));
        File.WriteAllText(Path.Combine(_source, "nested", "中文 file.txt"), "归档往返\ntext");
        File.WriteAllBytes(Path.Combine(_source, "empty.bin"), []);
        var bytes = new byte[200000]; new Random(123).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(_source, "random.bin"), bytes);
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);
    private string PathInRoot(string name) => Path.Combine(_root, name);
    private void AssertClean() => Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(_root)
        .Count(p => Path.GetFileName(p).StartsWith(".practical-tools-", StringComparison.Ordinal)));

    private static async Task<Exception> Failure(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) { return ex; }
        Assert.Fail("Operation should have failed."); return null!;
    }

    private void CompareTree(string restored)
    {
        var expected = Directory.GetFiles(_source, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(_source, p)).OrderBy(p => p).ToList();
        var actual = Directory.GetFiles(restored, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(restored, p)).OrderBy(p => p).ToList();
        CollectionAssert.AreEqual(expected, actual);
        foreach (var name in expected) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(_source, name)), File.ReadAllBytes(Path.Combine(restored, name)));
        Assert.IsTrue(Directory.Exists(Path.Combine(restored, "空目录")));
    }

    [TestMethod]
    public async Task WholeFolderRoundTripAndLegacyCompatibility()
    {
        var service = new FolderCryptoService();
        var encrypted = PathInRoot("source.zip.gpg"); var restored = PathInRoot("restored");
        var packaged = await service.EncryptAsync(_source, encrypted, _public);
        Assert.AreEqual(3, packaged.Files);
        var result = await service.DecryptAsync(encrypted, restored, _secret, "folder-password");
        Assert.AreEqual(3, result.Files); Assert.AreEqual(3, result.Directories);
        CompareTree(restored);
        // The new archive remains consumable by the old GPG + ZipFile pipeline.
        var plain = PathInRoot("legacy-reader.zip"); var legacyRestore = PathInRoot("legacy-reader");
        await GpgHelper.DecryptFileByPublicString(encrypted, plain, _secret, "folder-password");
        ZipFile.ExtractToDirectory(plain, legacyRestore); CompareTree(legacyRestore);
        // The original WPF CreateFromDirectory + GPG output remains readable by the new service.
        var legacyZip = PathInRoot("legacy-writer.zip"); var legacyEncrypted = PathInRoot("legacy.zip.gpg");
        ZipFile.CreateFromDirectory(_source, legacyZip, CompressionLevel.Optimal, includeBaseDirectory: false);
        await GpgHelper.EncryptFileByPublicString(legacyZip, legacyEncrypted, _public);
        await service.DecryptAsync(legacyEncrypted, PathInRoot("from-legacy"), _secret, "folder-password");
        CompareTree(PathInRoot("from-legacy")); AssertClean();
    }

    [TestMethod]
    public async Task EmptyRootDirectoryRoundTrips()
    {
        var empty = Directory.CreateDirectory(PathInRoot("empty")).FullName;
        var service = new FolderCryptoService();
        await service.EncryptAsync(empty, PathInRoot("empty.gpg"), _public);
        var result = await service.DecryptAsync(PathInRoot("empty.gpg"), PathInRoot("empty-restored"), _secret, "folder-password");
        Assert.AreEqual(0, result.Files); Assert.IsTrue(Directory.Exists(result.OutputPath));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(result.OutputPath).Length); AssertClean();
    }

    [TestMethod]
    public async Task ExistingTargetsAndNestedEncryptionOutputsAreRejected()
    {
        var service = new FolderCryptoService(); var output = PathInRoot("existing.gpg");
        await File.WriteAllTextAsync(output, "keep me");
        await Assert.ThrowsExceptionAsync<IOException>(() => service.EncryptAsync(_source, output, _public));
        Assert.AreEqual("keep me", await File.ReadAllTextAsync(output));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.EncryptAsync(_source, Path.Combine(_source, "inside.gpg"), _public));
        var encrypted = PathInRoot("valid.gpg"); await service.EncryptAsync(_source, encrypted, _public);
        await Assert.ThrowsExceptionAsync<IOException>(() => service.DecryptAsync(encrypted, _source, _secret, "folder-password"));
        AssertClean();
    }

    [TestMethod]
    [DataRow("打包", false)]
    [DataRow("PGP 加密", false)]
    [DataRow("发布结果", false)]
    [DataRow("PGP 解密及完整性校验", true)]
    [DataRow("解压", true)]
    [DataRow("发布结果", true)]
    public async Task CancellationCleansAllPlaintextAndNeverPublishesPartialOutput(string phase, bool decrypt)
    {
        var service = new FolderCryptoService(); var encrypted = PathInRoot("input.gpg");
        if (decrypt) await service.EncryptAsync(_source, encrypted, _public);
        var output = PathInRoot(decrypt ? "cancelled-directory" : "cancelled.gpg");
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            if (p.Phase == phase && (phase is not ("PGP 加密" or "PGP 解密及完整性校验") || p.Bytes > 0)) cts.Cancel();
        });
        var error = await Failure(() => decrypt
            ? service.DecryptAsync(encrypted, output, _secret, "folder-password", progress, cts.Token)
            : service.EncryptAsync(_source, output, _public, progress, cts.Token));
        Assert.IsInstanceOfType(error, typeof(OperationCanceledException));
        Assert.IsFalse(File.Exists(output)); Assert.IsFalse(Directory.Exists(output)); AssertClean();
    }

    [TestMethod]
    public async Task WrongPasswordAndTamperedPgpNeverExposeExtractedResults()
    {
        var service = new FolderCryptoService(); var encrypted = PathInRoot("input.gpg");
        await service.EncryptAsync(_source, encrypted, _public);
        var output = PathInRoot("wrong-password");
        await Failure(() => service.DecryptAsync(encrypted, output, _secret, "wrong-password"));
        Assert.IsFalse(Directory.Exists(output)); AssertClean();
        var bytes = await File.ReadAllBytesAsync(encrypted); bytes[^1] ^= 1; await File.WriteAllBytesAsync(encrypted, bytes);
        await Failure(() => service.DecryptAsync(encrypted, output, _secret, "folder-password"));
        Assert.IsFalse(Directory.Exists(output)); AssertClean();
    }

    private async Task<string> WrapZip(params (string Name, string Text, int Attributes)[] entries)
    {
        var zipPath = PathInRoot("crafted-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            foreach (var (name, text, attributes) in entries)
            {
                var entry = zip.CreateEntry(name); if (attributes != 0) entry.ExternalAttributes = attributes;
                using var writer = new StreamWriter(entry.Open()); writer.Write(text);
            }
        var encrypted = zipPath + ".gpg";
        await GpgHelper.EncryptFileByPublicString(zipPath, encrypted, _public); return encrypted;
    }

    [TestMethod]
    [DataRow("../escape.txt")]
    [DataRow("/absolute.txt")]
    [DataRow("C:/drive.txt")]
    [DataRow("..\\escape.txt")]
    [DataRow("safe/../escape.txt")]
    [DataRow("file:stream")]
    [DataRow("CON")]
    [DataRow("trailing. ")]
    public async Task ZipTraversalAndNonportableNamesAreRejectedBeforePublication(string name)
    {
        var encrypted = await WrapZip((name, "bad", 0)); var output = PathInRoot("unsafe");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new FolderCryptoService().DecryptAsync(encrypted, output, _secret, "folder-password"));
        Assert.IsFalse(Directory.Exists(output)); Assert.IsFalse(File.Exists(PathInRoot("escape.txt"))); AssertClean();
    }

    [TestMethod]
    public async Task DuplicateCaseAndFileDirectoryConflictsAndSymlinkMetadataAreRejected()
    {
        var cases = new[]
        {
            new[] { ("same", "one", 0), ("SAME", "two", 0) },
            new[] { ("a", "file", 0), ("a/b", "nested", 0) },
            new[] { ("é.txt", "one", 0), ("e\u0301.txt", "two", 0) },
            new[] { ("link", "../outside", unchecked((int)0xa1ff0000)) }
        };
        foreach (var entries in cases)
        {
            var encrypted = await WrapZip(entries); var output = PathInRoot("conflict");
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new FolderCryptoService().DecryptAsync(encrypted, output, _secret, "folder-password"));
            Assert.IsFalse(Directory.Exists(output)); AssertClean();
        }
    }

    [TestMethod]
    public async Task SizeAndEntryLimitsApplyBeforePublication()
    {
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new FolderCryptoService(new(MaxPlainBytes: 4))
            .EncryptAsync(_source, PathInRoot("too-large.gpg"), _public));
        var encrypted = await WrapZip(("a", "12345678", 0), ("b", "12345678", 0));
        foreach (var limits in new[] { new FolderCryptoLimits(MaxEntries: 1), new(MaxPlainBytes: 4), new(MaxZipBytes: 8) })
        {
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new FolderCryptoService(limits)
                .DecryptAsync(encrypted, PathInRoot("limited"), _secret, "folder-password"));
            Assert.IsFalse(Directory.Exists(PathInRoot("limited"))); AssertClean();
        }
    }

    [TestMethod]
    public async Task CentralDirectoryLyingAboutEntryCountIsRejected()
    {
        var encrypted = await WrapZip(("safe", "text", 0));
        var zip = encrypted[..^4];
        var bytes = await File.ReadAllBytesAsync(zip);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 8, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 10, 2), 0);
        await File.WriteAllBytesAsync(zip, bytes);
        File.Delete(encrypted); await GpgHelper.EncryptFileByPublicString(zip, encrypted, _public);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new FolderCryptoService()
            .DecryptAsync(encrypted, PathInRoot("lying"), _secret, "folder-password"));
        AssertClean();
    }

    [TestMethod]
    public async Task PgpWithoutIntegrityProtectionIsRejected()
    {
        var zip = PathInRoot("unprotected.zip");
        ZipFile.CreateFromDirectory(_source, zip);
        var encrypted = PathInRoot("unprotected.gpg");
        using (var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_public)))
        using (var output = File.Create(encrypted))
        {
            var generator = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, false, new SecureRandom());
            generator.AddMethod(GpgHelper.ReadPublicKey(input));
            using var encryptedStream = generator.Open(output, new byte[65536]);
            var literal = new PgpLiteralDataGenerator();
            using var data = literal.Open(encryptedStream, PgpLiteralData.Binary, "payload.zip", new FileInfo(zip).Length, DateTime.UtcNow);
            using var source = File.OpenRead(zip); source.CopyTo(data);
        }
        var error = await Failure(() => new FolderCryptoService().DecryptAsync(encrypted, PathInRoot("no-integrity"), _secret, "folder-password"));
        StringAssert.Contains(error.Message, "完整性");
        Assert.IsFalse(Directory.Exists(PathInRoot("no-integrity"))); AssertClean();
    }

    [TestMethod]
    public async Task Zip64DirectoryIsAcceptedWithoutCreatingThousandsOfFixtureEntries()
    {
        var encrypted = await WrapZip(("safe.txt", "zip64 content", 0));
        var zipPath = encrypted[..^4]; var original = File.ReadAllBytes(zipPath);
        var end = original.AsSpan(original.Length - 22, 22).ToArray();
        var record = new byte[56]; var locator = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0), 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(4), 44);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(12), 45);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(14), 45);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(40), BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(12)));
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(48), BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(16)));
        BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(0), 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator.AsSpan(8), (ulong)(original.Length - 22));
        BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), uint.MaxValue);
        await File.WriteAllBytesAsync(zipPath, original[..^22].Concat(record).Concat(locator).Concat(end).ToArray());
        File.Delete(encrypted); await GpgHelper.EncryptFileByPublicString(zipPath, encrypted, _public);
        var output = PathInRoot("zip64-restored");
        await new FolderCryptoService().DecryptAsync(encrypted, output, _secret, "folder-password");
        Assert.AreEqual("zip64 content", File.ReadAllText(Path.Combine(output, "safe.txt"))); AssertClean();
    }

    [TestMethod]
    public async Task DirectoryChangedDuringPackingIsNotPublished()
    {
        Directory.SetLastWriteTimeUtc(_source, DateTime.UtcNow.AddMinutes(-5));
        var changed = false;
        await Assert.ThrowsExceptionAsync<IOException>(() => new FolderCryptoService().EncryptAsync(_source, PathInRoot("changed.gpg"), _public,
            new InlineProgress(p =>
            {
                if (p.Phase != "打包" || changed) return;
                changed = true; File.WriteAllText(Path.Combine(_source, "new-file.txt"), "changed source");
            })));
        Assert.IsFalse(File.Exists(PathInRoot("changed.gpg"))); AssertClean();
    }

    [TestMethod]
    public async Task OutputCreatedAtPublicationBoundaryIsNeverOverwritten()
    {
        var output = PathInRoot("raced.gpg");
        await Assert.ThrowsExceptionAsync<IOException>(() => new FolderCryptoService().EncryptAsync(_source, output, _public,
            new InlineProgress(p => { if (p.Phase == "发布结果") File.WriteAllText(output, "keep raced output"); })));
        Assert.AreEqual("keep raced output", File.ReadAllText(output)); AssertClean();
    }

    [TestMethod]
    public async Task WorkspaceAndRestoredRootUsePrivatePermissions()
    {
        var seen = false;
        var service = new FolderCryptoService();
        await service.EncryptAsync(_source, PathInRoot("private.gpg"), _public, new InlineProgress(p =>
        {
            if (p.Phase != "打包") return;
            foreach (var root in Directory.GetDirectories(_root, ".practical-tools-folder-*"))
            {
                seen = true;
                if (OperatingSystem.IsWindows())
                {
                    var acl = new DirectoryInfo(root).GetAccessControl();
                    Assert.IsTrue(acl.AreAccessRulesProtected);
                    using var identity = WindowsIdentity.GetCurrent();
                    var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
                    foreach (var rule in rules) Assert.IsTrue(rule.IdentityReference.Equals(identity.User));
                }
                else Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
            }
        }));
        Assert.IsTrue(seen);
        var output = PathInRoot("private-restored");
        await service.DecryptAsync(PathInRoot("private.gpg"), output, _secret, "folder-password");
        if (OperatingSystem.IsWindows()) Assert.IsTrue(new DirectoryInfo(output).GetAccessControl().AreAccessRulesProtected);
        else Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(output));
        AssertClean();
    }
}
