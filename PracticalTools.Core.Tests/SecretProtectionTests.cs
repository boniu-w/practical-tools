using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.GPG;
using PracticalTools.Security;
using PracticalTools.Sqlite;
using PracticalTools.Storage;
using PracticalTools.util;

namespace PracticalTools.Core.Tests;

[TestClass]
public class SecretProtectionTests
{
    private AppDataPaths _paths = null!;
    private LocalAesSecretProtector _protector = null!;

    [TestInitialize]
    public void Setup()
    {
        SQLitePCL.Batteries_V2.Init();
        _paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), "pt-protection-tests", Guid.NewGuid().ToString("N")));
        _paths.EnsureDirectories();
        _protector = new LocalAesSecretProtector(_paths.SecretKeyPath);
        _protector.InitializeKey(allowCreate: true);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_paths.Root, recursive: true);
    }

    private static MyDbContext Open(AppDataPaths paths, ISecretProtector? protector) => new(
        new DbContextOptionsBuilder<MyDbContext>()
            .UseSqlite(DatabaseInitializer.ConnectionString(paths.DatabasePath)).Options, protector);

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("口令 🔑 with spaces\n和换行")]
    public void ValuesRoundTripAfterProtectorRestart(string plaintext)
    {
        var encrypted = _protector.Protect(plaintext);
        var restarted = new LocalAesSecretProtector(_paths.SecretKeyPath);
        restarted.InitializeKey(allowCreate: false);
        Assert.AreEqual(plaintext, restarted.Unprotect(encrypted));
        if (plaintext.Length > 0)
        {
            StringAssert.StartsWith(encrypted, LocalAesSecretProtector.Prefix);
            Assert.AreNotEqual(plaintext, encrypted);
        }
    }

    [TestMethod]
    public void EveryWriteUsesFreshRandomNonce()
    {
        var nonces = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var encrypted = _protector.Protect("same value");
            var bytes = Convert.FromBase64String(encrypted[LocalAesSecretProtector.Prefix.Length..]);
            Assert.IsTrue(nonces.Add(Convert.ToHexString(bytes.AsSpan(0, 12))));
        }
    }

    [TestMethod]
    [DataRow(0)] // nonce
    [DataRow(12)] // authentication tag
    [DataRow(28)] // ciphertext
    public void TamperingIsRejectedWithoutPlaintextFallback(int offset)
    {
        var encrypted = _protector.Protect("protected value");
        var bytes = Convert.FromBase64String(encrypted[LocalAesSecretProtector.Prefix.Length..]);
        bytes[offset] ^= 1;
        Assert.ThrowsException<AuthenticationTagMismatchException>(() =>
            _protector.Unprotect(LocalAesSecretProtector.Prefix + Convert.ToBase64String(bytes)));
    }

    [TestMethod]
    [DataRow("plaintext from legacy database")]
    [DataRow("pt-aesgcm:v2:AAAA")]
    [DataRow("pt-aesgcm:v1:!invalid!")]
    [DataRow("pt-aesgcm:v1:AAAA")]
    public void UnsupportedAndMalformedValuesFailExplicitly(string value) =>
        Assert.ThrowsException<CryptographicException>(() => _protector.Unprotect(value));

    [TestMethod]
    public void WrongKeyAndMissingKeyNeverProduceReplacementOrPlaintext()
    {
        var encrypted = _protector.Protect("private passphrase");
        var original = File.ReadAllBytes(_paths.SecretKeyPath);
        File.WriteAllBytes(_paths.SecretKeyPath, RandomNumberGenerator.GetBytes(32));
        Assert.ThrowsException<AuthenticationTagMismatchException>(() => _protector.Unprotect(encrypted));
        File.WriteAllBytes(_paths.SecretKeyPath, original);
        Assert.AreEqual("private passphrase", _protector.Unprotect(encrypted));
        File.Delete(_paths.SecretKeyPath);
        Assert.ThrowsException<FileNotFoundException>(() => _protector.Protect("value"));
        Assert.ThrowsException<FileNotFoundException>(() => _protector.Unprotect(encrypted));
        Assert.ThrowsException<CryptographicException>(() => _protector.InitializeKey(allowCreate: false));
        Assert.IsFalse(File.Exists(_paths.SecretKeyPath));
    }

    [TestMethod]
    public void CorruptKeyIsRejectedAndInitializationDoesNotOverwriteIt()
    {
        File.WriteAllBytes(_paths.SecretKeyPath, new byte[3]);
        Assert.ThrowsException<CryptographicException>(() => _protector.InitializeKey(allowCreate: true));
        Assert.AreEqual(3L, new FileInfo(_paths.SecretKeyPath).Length);
    }

    [TestMethod]
    public void KeyFileIsPrivateToCurrentUserAtCreation()
    {
        if (OperatingSystem.IsWindows())
        {
            using var user = WindowsIdentity.GetCurrent();
            var acl = new FileInfo(_paths.SecretKeyPath).GetAccessControl();
            Assert.IsTrue(acl.AreAccessRulesProtected);
            var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual(user.User, rules[0].IdentityReference);
            Assert.AreEqual(AccessControlType.Allow, rules[0].AccessControlType);
        }
        else
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_paths.SecretKeyPath));
    }

    [TestMethod]
    public void DatabaseStoresVersionedCiphertextAndRestoresAfterRestart()
    {
        using (var db = Open(_paths, _protector))
        {
            DatabaseInitializer.InitializeProtectedStore(db, _protector);
            db.GpgFile.Add(new GpgFile { Id = 1, Passphrase = "数据库口令" });
            db.SaveChanges();
        }
        using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString(_paths.DatabasePath)))
        {
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT Passphrase FROM GpgFile WHERE Id = 1";
            var stored = (string)query.ExecuteScalar()!;
            StringAssert.StartsWith(stored, LocalAesSecretProtector.Prefix);
            Assert.IsFalse(stored.Contains("数据库口令"));
        }
        var restarted = new LocalAesSecretProtector(_paths.SecretKeyPath);
        using (var db = Open(_paths, restarted))
        {
            DatabaseInitializer.InitializeProtectedStore(db, restarted);
            Assert.AreEqual("数据库口令", db.GpgFile.Single().Passphrase);
        }
        File.Delete(_paths.SecretKeyPath);
        using var missingKey = Open(_paths, restarted);
        Assert.ThrowsException<CryptographicException>(() => DatabaseInitializer.InitializeProtectedStore(missingKey, restarted));
        Assert.IsFalse(File.Exists(_paths.SecretKeyPath));
    }

    [TestMethod]
    public void EfModelCacheDoesNotReuseOtherDataDirectorysProtector()
    {
        var other = new AppDataPaths(Path.Combine(_paths.Root, "other"));
        other.EnsureDirectories();
        var otherProtector = new LocalAesSecretProtector(other.SecretKeyPath);
        otherProtector.InitializeKey(allowCreate: true);
        using (var db = Open(_paths, _protector))
        {
            DatabaseInitializer.InitializeProtectedStore(db, _protector);
            db.GpgFile.Add(new GpgFile { Id = 1, Passphrase = "first" });
            db.SaveChanges();
        }
        using (var db = Open(other, otherProtector))
        {
            DatabaseInitializer.InitializeProtectedStore(db, otherProtector);
            db.GpgFile.Add(new GpgFile { Id = 1, Passphrase = "second" });
            db.SaveChanges();
        }
        using (var first = Open(_paths, new LocalAesSecretProtector(_paths.SecretKeyPath)))
            Assert.AreEqual("first", first.GpgFile.Single().Passphrase);
        using (var second = Open(other, new LocalAesSecretProtector(other.SecretKeyPath)))
            Assert.AreEqual("second", second.GpgFile.Single().Passphrase);
        using var wrong = Open(other, _protector);
        Assert.ThrowsException<AuthenticationTagMismatchException>(() => wrong.GpgFile.Single());
    }

    [TestMethod]
    public void KeyCreationIsAtomicAndDoesNotReplaceAnExistingKey()
    {
        var path = Path.Combine(_paths.Root, "concurrent.key");
        Parallel.For(0, 8, _ => new LocalAesSecretProtector(path).InitializeKey(allowCreate: true));
        var first = new LocalAesSecretProtector(path);
        var ciphertext = first.Protect("keep this key");
        var saved = File.ReadAllBytes(path);
        first.InitializeKey(allowCreate: true);
        CollectionAssert.AreEqual(saved, File.ReadAllBytes(path));
        Assert.AreEqual("keep this key", new LocalAesSecretProtector(path).Unprotect(ciphertext));
        Assert.AreEqual(0, Directory.GetFiles(_paths.Root, "*.tmp").Length);
    }

    [TestMethod]
    public void MatchingDatabaseAndKeyBackupCanBeRestoredInAnotherDataDirectory()
    {
        using (var db = Open(_paths, _protector))
        {
            DatabaseInitializer.InitializeProtectedStore(db, _protector);
            db.GpgFile.Add(new GpgFile { Id = 1, Passphrase = "restore me" });
            db.SaveChanges();
        }
        SqliteConnection.ClearAllPools();
        var restored = new AppDataPaths(Path.Combine(_paths.Root, "restored"));
        restored.EnsureDirectories();
        File.Copy(_paths.DatabasePath, restored.DatabasePath);
        File.Copy(_paths.SecretKeyPath, restored.SecretKeyPath);
        var restoredProtector = new LocalAesSecretProtector(restored.SecretKeyPath);
        using var restoredDb = Open(restored, restoredProtector);
        DatabaseInitializer.InitializeProtectedStore(restoredDb, restoredProtector);
        Assert.AreEqual("restore me", restoredDb.GpgFile.Single().Passphrase);
    }

    [TestMethod]
    public void DefaultWpfContextStillUsesDpapiAndLegacyPlaintextCompatibility()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.ThrowsException<PlatformNotSupportedException>(() => LegacyDpapiSecretProtector.Instance.Protect("value"));
            return;
        }
        var legacyCiphertext = CryptoHelper.Encrypt("original password");
        using (var db = Open(_paths, null))
        {
            DatabaseInitializer.Initialize(db);
            db.GpgFile.Add(new GpgFile { Id = 1, Passphrase = "original password" });
            db.SaveChanges();
        }
        using (var db = Open(_paths, null)) Assert.AreEqual("original password", db.GpgFile.Single().Passphrase);
        Assert.AreEqual("original password", LegacyDpapiSecretProtector.Instance.Unprotect(legacyCiphertext));
        Assert.AreEqual("old plaintext", LegacyDpapiSecretProtector.Instance.Unprotect("old plaintext"));
        Assert.ThrowsException<CryptographicException>(() => _protector.Unprotect(legacyCiphertext));
        using var aesContext = Open(_paths, _protector);
        Assert.ThrowsException<CryptographicException>(() => aesContext.GpgFile.Single());
    }
}
