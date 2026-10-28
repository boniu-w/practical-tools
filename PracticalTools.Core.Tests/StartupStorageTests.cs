using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.Activation.Service;
using PracticalTools.Sqlite;
using PracticalTools.Storage;

namespace PracticalTools.Core.Tests;

[TestClass]
public class StartupStorageTests
{
    private AppDataPaths _paths = null!;

    [TestInitialize]
    public void Setup()
    {
        SQLitePCL.Batteries_V2.Init();
        _paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), "pt-storage-tests", Guid.NewGuid().ToString("N")));
        _paths.EnsureDirectories();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_paths.Root, recursive: true);
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(_paths.DatabasePath)));
        services.AddSingleton<ActivationService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void Initialize(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        DatabaseInitializer.Initialize(scope.ServiceProvider.GetRequiredService<MyDbContext>());
    }

    [TestMethod]
    public void ActivationPersistsAcrossServiceProviderRestarts()
    {
        using (var provider = CreateProvider())
        {
            Initialize(provider);
            var activation = provider.GetRequiredService<ActivationService>();
            Assert.IsFalse(activation.IsActivated());
            Assert.IsFalse(activation.Activate("invalid-code"));
            Assert.IsFalse(activation.IsActivated());
            Assert.IsTrue(activation.Activate("  GPGKEY-UTIL-PRO-VIP  "));
        }
        using (var provider = CreateProvider())
        {
            Initialize(provider);
            Initialize(provider);
            Assert.IsTrue(provider.GetRequiredService<ActivationService>().IsActivated());
            using var scope = provider.CreateScope();
            var records = scope.ServiceProvider.GetRequiredService<MyDbContext>().ActivationRecord.ToList();
            Assert.AreEqual(1, records.Count);
            Assert.IsNotNull(records[0].ActivateTime);
        }
        SqliteConnection.ClearAllPools();
        var header = File.ReadAllBytes(_paths.DatabasePath).Take(16).ToArray();
        Assert.AreNotEqual("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(header));
    }

    [TestMethod]
    public void ExistingDatabaseGetsMissingActivationTableWithoutLosingOtherTables()
    {
        using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString(_paths.DatabasePath)))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE ExistingData (Value TEXT); INSERT INTO ExistingData VALUES ('preserve');";
            cmd.ExecuteNonQuery();
        }
        using var provider = CreateProvider();
        Initialize(provider);
        Assert.IsFalse(provider.GetRequiredService<ActivationService>().IsActivated());
        using var check = new SqliteConnection(DatabaseInitializer.ConnectionString(_paths.DatabasePath));
        check.Open();
        using var query = check.CreateCommand();
        query.CommandText = "SELECT Value FROM ExistingData";
        Assert.AreEqual("preserve", query.ExecuteScalar());
    }

    [TestMethod]
    public void ExclusiveLeaseRejectsSecondInstanceAndCanBeReacquiredAfterExit()
    {
        using (var first = SingleInstanceLease.Acquire(_paths.InstanceLockPath))
            Assert.ThrowsException<IOException>(() => SingleInstanceLease.Acquire(_paths.InstanceLockPath));
        using var next = SingleInstanceLease.Acquire(_paths.InstanceLockPath);
        Assert.IsTrue(File.Exists(_paths.InstanceLockPath));
    }

    [TestMethod]
    public void ConnectionStringHandlesSemicolonsAndApostrophesInUserDirectory()
    {
        var unusual = Path.Combine(_paths.Root, "user';name;", "db.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(unusual)!);
        using var db = new MyDbContext(new DbContextOptionsBuilder<MyDbContext>()
            .UseSqlite(DatabaseInitializer.ConnectionString(unusual)).Options);
        DatabaseInitializer.Initialize(db);
        Assert.IsFalse(db.ActivationRecord.Any());
        Assert.IsTrue(File.Exists(unusual));
    }
}
