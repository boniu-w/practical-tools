using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.Activation.Model;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.FileManager.Model;
using PracticalTools.GPG;
using PracticalTools.Migration;
using PracticalTools.Security;
using PracticalTools.Sqlite;
using PracticalTools.Storage;

namespace PracticalTools.Core.Tests;

[TestClass]
public class MigrationTests
{
    private const string Password = "migration-password-2026";
    private AppDataPaths _paths = null!;
    private ServiceProvider _provider = null!;
    private static string _public = null!, _secret = null!;
    private KeyLibrary Library => _provider.GetRequiredService<KeyLibrary>();
    private MigrationPackageService Packages => _provider.GetRequiredService<MigrationPackageService>();

    [ClassInitialize]
    public static void Keys(TestContext context)
    {
        var pair = GpgHelper.GeneratePgpKeyPair("migration tests", "key-password", 2048);
        _public = GpgHelper.ExportPublicKey(pair.pubRing);
        _secret = GpgHelper.ExportPrivateKey(pair.secRing);
    }

    [TestInitialize]
    public void Setup()
    {
        SQLitePCL.Batteries_V2.Init();
        _paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), "pt-migration-tests", Guid.NewGuid().ToString("N")));
        _paths.EnsureDirectories();
        var services = new ServiceCollection();
        services.AddSingleton<LocalAesSecretProtector>(_ => new LocalAesSecretProtector(_paths.SecretKeyPath));
        services.AddSingleton<ISecretProtector>(p => p.GetRequiredService<LocalAesSecretProtector>());
        services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(_paths.DatabasePath)));
        services.AddSingleton<KeyLibrary>();
        services.AddSingleton<MigrationPackageService>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = _provider.CreateScope();
        DatabaseInitializer.InitializeProtectedStore(scope.ServiceProvider.GetRequiredService<MyDbContext>(),
            _provider.GetRequiredService<LocalAesSecretProtector>());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _provider.Dispose(); SqliteConnection.ClearAllPools();
        Directory.Delete(_paths.Root, recursive: true);
    }

    private static MigrationSnapshot Snapshot() => new()
    {
        Keys = [new GpgFile { Id = 71, Identity = "migration tests", IdentityName = "preserved name",
            Description = "preserved description", PublicKey = _public, SecretKey = _secret, Passphrase = "key-password" }],
        Blacklist = [new BlacklistEntry { Id = 81, Author = "preserved author", WorksTime = new DateTime(2020, 1, 2) }],
        Scans = [new DuplicateFilesEntry { Id = "scan-id", FileName = "original.txt", FilePath = @"C:\old\original.txt" }],
        Files = [new MyFile { Id = "file-id", FileName = "file.txt", FilePath = @"C:\old\file.txt" }]
    };

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires the original Windows user / DPAPI.");
    }

    private string CreateLegacy(bool encrypted, bool plainPassword)
    {
        var path = Path.Combine(_paths.Root, "legacy.db");
        var cs = new SqliteConnectionStringBuilder
        { DataSource = path, Password = encrypted ? PracticalTools.Constant.CommonConst.DbPassword : "", Pooling = false }.ToString();
        using (var db = new MyDbContext(new DbContextOptionsBuilder<MyDbContext>().UseSqlite(cs).Options))
        {
            db.Database.EnsureCreated();
            var snapshot = Snapshot();
            db.GpgFile.AddRange(snapshot.Keys);
            db.Blacklist.AddRange(snapshot.Blacklist);
            db.DuplicateFiles.AddRange(snapshot.Scans);
            db.MyFile.AddRange(snapshot.Files);
            db.ActivationRecord.Add(new ActivationRecord { ActivationCode = "old-activation", ActivateTime = DateTime.Now });
            db.SaveChanges();
        }
        if (plainPassword)
        {
            using var connection = new SqliteConnection(cs);
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE GpgFile SET Passphrase='key-password'";
            update.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public async Task LegacyReadOnlyExportAndImportPreserveDataAndSkipDuplicates(bool encrypted, bool plainPassword)
    {
        RequireWindows();
        var source = CreateLegacy(encrypted, plainPassword);
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var package = Path.Combine(_paths.Root, "transfer.ptmigration");
        await Packages.ExportLegacyAsync(source, package, Password);
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(source)));
        var preview = await Packages.PreviewAsync(package, Password);
        Assert.AreEqual(1, preview.Keys);
        Assert.AreEqual(1, preview.Blacklist);
        Assert.AreEqual(1, preview.Scans);
        Assert.AreEqual(1, preview.Files);
        Assert.AreEqual(GpgHelper.GetFingerprint(_public), preview.Fingerprints.Single());
        var result = await Packages.ImportAsync(package, Password, preview.Digest);
        Assert.AreEqual(new MigrationResult(1, 1, 1, 1, 0), result);
        var target = (await Library.ListAsync()).Single();
        Assert.AreEqual("preserved name", target.Name);
        Assert.AreEqual("preserved description", target.Description);
        Assert.AreEqual("key-password", (await Library.GetSecretAsync(target.Id)).Passphrase);
        var duplicate = await Packages.ImportAsync(package, Password, preview.Digest);
        Assert.AreEqual(new MigrationResult(0, 0, 0, 0, 4), duplicate);
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.AreEqual(0, db.ActivationRecord.Count());
        Assert.AreEqual("preserved author", db.Blacklist.Single().Author);
        Assert.AreEqual(@"C:\old\file.txt", db.MyFile.Single().FilePath);
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(source)));
    }

    [TestMethod]
    public async Task LegacyMinimalSchemaAndMissingOptionalTablesRemainUnchanged()
    {
        RequireWindows();
        var path = Path.Combine(_paths.Root, "旧库 ;'.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE GpgFile (Id INTEGER, PublicKey TEXT, SecretKey TEXT, Passphrase TEXT, Identity TEXT); INSERT INTO GpgFile VALUES(1,$public,$secret,'key-password','minimal identity')";
            command.Parameters.AddWithValue("$public", _public);
            command.Parameters.AddWithValue("$secret", _secret);
            command.ExecuteNonQuery();
        }
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var package = Path.Combine(_paths.Root, "minimal.ptmigration");
        await Packages.ExportLegacyAsync(path, package, Password);
        var preview = await Packages.PreviewAsync(package, Password);
        Assert.AreEqual(0, preview.Blacklist + preview.Scans + preview.Files);
        await Packages.ImportAsync(package, Password, preview.Digest);
        Assert.AreEqual("minimal identity", (await Library.ListAsync()).Single().Name);
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [TestMethod]
    public async Task UnknownDatabaseAndSnapshotVersionAreRejected()
    {
        var unsupported = Snapshot();
        unsupported.SchemaVersion = 999;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Library.ImportMigrationSnapshotAsync(unsupported));
        Assert.AreEqual(0, (await Library.ListAsync()).Count);
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_paths.Root, "unrelated.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Unrelated (Id INTEGER)";
            command.ExecuteNonQuery();
        }
        var target = Path.Combine(_paths.Root, "unrelated.ptmigration");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Packages.ExportLegacyAsync(path, target, Password));
        Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public async Task TamperedDpapiFailsWithoutFallbackAndWithoutProducingPackage()
    {
        RequireWindows();
        var path = CreateLegacy(false, false);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE GpgFile SET Passphrase=$value";
            // A recognizable DPAPI provider header with an invalid/truncated encrypted body.
            update.Parameters.AddWithValue("$value", Convert.ToBase64String(Convert.FromHexString("01000000D08C9DDF0115D1118C7A00C04FC297EB")));
            update.ExecuteNonQuery();
        }
        var target = Path.Combine(_paths.Root, "must-not-exist.ptmigration");
        await Assert.ThrowsExceptionAsync<CryptographicException>(() => Packages.ExportLegacyAsync(path, target, Password));
        Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public async Task PackagePasswordTamperingAndVersionErrorsWriteNothing()
    {
        await Library.ImportMigrationSnapshotAsync(Snapshot());
        var path = Path.Combine(_paths.Root, "package.ptmigration");
        await Packages.ExportCurrentAsync(path, Password);
        var bytes = File.ReadAllBytes(path);
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(bytes).Contains("key-password"));
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(bytes).Contains("BEGIN PGP"));
        await Assert.ThrowsExceptionAsync<CryptographicException>(() => Packages.PreviewAsync(path, "wrong-password-2026"));
        bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes);
        await Assert.ThrowsExceptionAsync<CryptographicException>(() => Packages.PreviewAsync(path, Password));
        bytes[0] ^= 1;
        File.WriteAllBytes(path, bytes);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Packages.PreviewAsync(path, Password));
        Assert.AreEqual(1, (await Library.ListAsync()).Count);
    }

    [TestMethod]
    public async Task DatabaseFailureRollsBackAllTablesAndLeavesActivationUntouched()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            db.ActivationRecord.Add(new ActivationRecord { ActivationCode = "current-activation" });
            db.SaveChanges();
            db.Database.ExecuteSqlRaw("CREATE TRIGGER migration_failure BEFORE INSERT ON MyFile BEGIN SELECT RAISE(ABORT,'injected failure'); END;");
        }
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => Library.ImportMigrationSnapshotAsync(Snapshot()));
        using var check = _provider.CreateScope();
        var database = check.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.AreEqual(0, database.GpgFile.Count());
        Assert.AreEqual(0, database.Blacklist.Count());
        Assert.AreEqual(0, database.DuplicateFiles.Count());
        Assert.AreEqual(0, database.MyFile.Count());
        Assert.AreEqual("current-activation", database.ActivationRecord.Single().ActivationCode);
    }

    [TestMethod]
    public async Task InvalidLaterKeyDoesNotImportEarlierValidKeyOrOtherRecords()
    {
        var snapshot = Snapshot();
        snapshot.Keys.Add(new GpgFile { Id = 72, PublicKey = "invalid key" });
        Exception? failure = null;
        try { await Library.ImportMigrationSnapshotAsync(snapshot); } catch (Exception ex) { failure = ex; }
        Assert.IsNotNull(failure);
        Assert.AreEqual(0, (await Library.ListAsync()).Count);
        using var check = _provider.CreateScope();
        Assert.AreEqual(0, check.ServiceProvider.GetRequiredService<MyDbContext>().Blacklist.Count());
    }

    [TestMethod]
    public async Task ExportDoesNotOverwriteAndPreviewDigestDetectsReplacement()
    {
        var original = Path.Combine(_paths.Root, "original.ptmigration");
        var replacement = Path.Combine(_paths.Root, "replacement.ptmigration");
        await Packages.ExportCurrentAsync(original, Password);
        var preview = await Packages.PreviewAsync(original, Password);
        var saved = File.ReadAllBytes(original);
        await Assert.ThrowsExceptionAsync<IOException>(() => Packages.ExportCurrentAsync(original, Password));
        CollectionAssert.AreEqual(saved, File.ReadAllBytes(original));
        await Packages.ExportCurrentAsync(replacement, Password);
        File.Copy(replacement, original, overwrite: true);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Packages.ImportAsync(original, Password, preview.Digest));
        Assert.AreEqual(0, Directory.GetFiles(_paths.Root, "*.tmp").Length);
    }

    [TestMethod]
    public async Task ExistingPublicKeyAndMetadataAreNotOverwrittenByMigrationPrivateKey()
    {
        await Library.ImportAsync(_public, "", "keep current name", "keep description");
        var result = await Library.ImportMigrationSnapshotAsync(Snapshot());
        Assert.AreEqual(0, result.KeysAdded);
        Assert.AreEqual(1, result.Skipped);
        var current = (await Library.ListAsync()).Single();
        Assert.AreEqual("keep current name", current.Name);
        Assert.IsFalse(current.HasSecret);
    }
}
