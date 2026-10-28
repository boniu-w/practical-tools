using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.GPG;
using PracticalTools.Security;
using PracticalTools.Sqlite;
using PracticalTools.Storage;

namespace PracticalTools.Core.Tests;

[TestClass]
public class KeyLibraryTests
{
    private AppDataPaths _paths = null!;
    private ServiceProvider _provider = null!;
    private static string _public = null!, _secret = null!;
    private KeyLibrary Library => _provider.GetRequiredService<KeyLibrary>();

    [ClassInitialize]
    public static void Keys(TestContext context)
    {
        var pair = GpgHelper.GeneratePgpKeyPair("library tests <test@example.com>", "test-password", 2048);
        _public = GpgHelper.ExportPublicKey(pair.pubRing);
        _secret = GpgHelper.ExportPrivateKey(pair.secRing);
    }

    [TestInitialize]
    public void Setup()
    {
        SQLitePCL.Batteries_V2.Init();
        _paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), "pt-library-tests", Guid.NewGuid().ToString("N")));
        _paths.EnsureDirectories();
        _provider = CreateProvider();
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LocalAesSecretProtector>(_ => new LocalAesSecretProtector(_paths.SecretKeyPath));
        services.AddSingleton<ISecretProtector>(p => p.GetRequiredService<LocalAesSecretProtector>());
        services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(_paths.DatabasePath)));
        services.AddSingleton<KeyLibrary>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        DatabaseInitializer.InitializeProtectedStore(scope.ServiceProvider.GetRequiredService<MyDbContext>(),
            provider.GetRequiredService<LocalAesSecretProtector>());
        return provider;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_paths.Root, recursive: true);
    }

    [TestMethod]
    public async Task GenerateSaveRestartSelectAndEncryptDecrypt()
    {
        var saved = await Library.GenerateAsync("roundtrip", "protected password", 2048, 30);
        Assert.IsTrue(saved.HasSecret);
        Assert.IsNotNull(saved.EndsOn);
        _provider.Dispose();
        _provider = CreateProvider();
        var stored = (await Library.ListAsync()).Single();
        Assert.AreEqual(saved.Fingerprint, stored.Fingerprint);
        Assert.AreEqual(DateTimeKind.Utc, stored.EndsOn!.Value.Kind);
        var input = Path.Combine(_paths.Root, "原文.bin");
        var encrypted = Path.Combine(_paths.Root, "encrypted.gpg");
        var output = Path.Combine(_paths.Root, "decrypted.bin");
        var bytes = Enumerable.Range(0, 10000).Select(i => (byte)(i % 256)).ToArray();
        await File.WriteAllBytesAsync(input, bytes);
        await GpgFileOperations.EncryptAsync(input, encrypted, await Library.GetPublicKeyAsync(stored.Id));
        var material = await Library.GetSecretAsync(stored.Id);
        await GpgFileOperations.DecryptAsync(encrypted, output, material.Key, material.Passphrase);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(output));
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString(_paths.DatabasePath));
        connection.Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT Passphrase FROM GpgFile";
        StringAssert.StartsWith((string)query.ExecuteScalar()!, LocalAesSecretProtector.Prefix);
    }

    [TestMethod]
    public async Task PublicImportCanUpgradeWithMatchingPrivateKeyButNeverOverwriteExistingPrivateKey()
    {
        var pub = await Library.ImportAsync(_public, "ignored", "public only", "first");
        Assert.IsFalse(pub.HasSecret);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Library.GetSecretAsync(pub.Id));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Library.ImportAsync(_public, "", "", ""));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Library.ImportAsync(_secret, "wrong", "", ""));
        Assert.IsFalse((await Library.ListAsync()).Single().HasSecret);
        var upgraded = await Library.ImportAsync(_secret, "test-password", "private upgraded", "");
        Assert.AreEqual(pub.Id, upgraded.Id);
        Assert.IsTrue(upgraded.HasSecret);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Library.ImportAsync(_secret, "test-password", "", ""));
        Assert.AreEqual(1, (await Library.ListAsync()).Count);
    }

    [TestMethod]
    public async Task MetadataUpdatePreservesKeyAndPasswordAndRejectsEmptyNames()
    {
        var key = await Library.ImportAsync(_secret, "test-password", "name", "description");
        Assert.IsTrue(await Library.UpdateMetadataAsync(key.Id, " renamed ", " edited "));
        var summary = (await Library.ListAsync()).Single();
        Assert.AreEqual("renamed", summary.Name);
        Assert.AreEqual("edited", summary.Description);
        Assert.AreEqual(key.Fingerprint, summary.Fingerprint);
        Assert.AreEqual("test-password", (await Library.GetSecretAsync(key.Id)).Passphrase);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Library.UpdateMetadataAsync(key.Id, " ", ""));
        Assert.AreEqual("renamed", (await Library.ListAsync()).Single().Name);
        Assert.IsFalse(await Library.UpdateMetadataAsync(-1, "missing", ""));
    }

    [TestMethod]
    public async Task ExportNeverOverwritesAndDeletedIdsFailClearly()
    {
        var key = await Library.ImportAsync(_secret, "test-password", "", "");
        var pubPath = Path.Combine(_paths.Root, "public.asc");
        var secPath = Path.Combine(_paths.Root, "secret.asc");
        await Library.ExportAsync(key.Id, pubPath, false);
        await Library.ExportAsync(key.Id, secPath, true);
        Assert.AreEqual(key.Fingerprint, GpgHelper.GetFingerprint(await File.ReadAllTextAsync(pubPath)));
        var original = await File.ReadAllTextAsync(secPath);
        await Assert.ThrowsExceptionAsync<IOException>(() => Library.ExportAsync(key.Id, secPath, true));
        Assert.AreEqual(original, await File.ReadAllTextAsync(secPath));
        Assert.AreEqual(0, Directory.GetFiles(_paths.Root, "*.tmp").Length);
        Assert.IsTrue(await Library.DeleteAsync(key.Id));
        Assert.IsFalse(await Library.DeleteAsync(key.Id));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Library.GetPublicKeyAsync(key.Id));
        Assert.AreEqual(0, (await Library.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ListAndMetadataDoNotDecryptPasswordsOrLoadPrivateKeyMaterial()
    {
        var key = await Library.ImportAsync(_secret, "test-password", "safe list", "");
        File.Delete(_paths.SecretKeyPath);
        Assert.AreEqual("safe list", (await Library.ListAsync()).Single().Name);
        Assert.IsTrue(await Library.UpdateMetadataAsync(key.Id, "metadata only", ""));
        Assert.AreEqual(key.Fingerprint, GpgHelper.GetFingerprint(await Library.GetPublicKeyAsync(key.Id)));
        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => Library.GetSecretAsync(key.Id));
    }

    [TestMethod]
    public async Task InvalidImportsAndGenerationSettingsWriteNothing()
    {
        // Parsing failures may originate in BouncyCastle, but must never commit a record.
        try { await Library.ImportAsync("not a key", "", "", ""); Assert.Fail("Invalid key accepted."); }
        catch (AssertFailedException) { throw; }
        catch (Exception) { }
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Library.GenerateAsync("name", "password", 1024, 1));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Library.GenerateAsync("name", "password", 2048, -1));
        Assert.AreEqual(0, (await Library.ListAsync()).Count);
    }

    [TestMethod]
    public void GpgServiceUsesOneSequenceAcrossRapidRecordCreation()
    {
        using var scope = _provider.CreateScope();
        var service = new GpgFileService(scope.ServiceProvider.GetRequiredService<MyDbContext>());
        var ids = Enumerable.Range(0, 3000).Select(_ => service.GenerateGpgFile("name", "").Id).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count());
    }
}
