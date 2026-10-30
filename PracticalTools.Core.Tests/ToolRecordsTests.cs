using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.Blacklist.Model;
using PracticalTools.Security;
using PracticalTools.Sqlite;
using PracticalTools.Storage;

namespace PracticalTools.Core.Tests;

[TestClass]
public class ToolRecordsTests
{
    private string _root = null!;
    private ServiceProvider _provider = null!;
    private ToolRecords Store => _provider.GetRequiredService<ToolRecords>();
    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("pt-records-").FullName;
        SQLitePCL.Batteries_V2.Init(); var paths = new AppDataPaths(_root); paths.EnsureDirectories();
        var services = new ServiceCollection();
        services.AddSingleton<ISecretProtector>(new LocalAesSecretProtector(paths.SecretKeyPath));
        services.AddDbContext<MyDbContext>(o => o.UseSqlite(DatabaseInitializer.ConnectionString(paths.DatabasePath)));
        services.AddSingleton<ToolRecords>(); _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope(); scope.ServiceProvider.GetRequiredService<MyDbContext>().Database.EnsureCreated();
    }
    [TestCleanup] public void Cleanup() { _provider.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); }
    [TestMethod]
    public async Task FileRecordsPersistDeduplicateEditAndDeleteWithoutTouchingSource()
    {
        var path = Path.Combine(_root, "中文.txt"); File.WriteAllText(path, "keep");
        await Store.AddFilesAsync([path, path]); await Store.AddFilesAsync([path]);
        var record = (await Store.FilesAsync("中文")).Single(); Assert.IsFalse(string.IsNullOrEmpty(record.Md5Value));
        await Store.UpdateFileAsync(record.Id, "文档");
        Assert.AreEqual("文档", (await new ToolRecords(_provider.GetRequiredService<IServiceScopeFactory>()).FilesAsync("")).Single().Type);
        await Store.DeleteFileAsync(record.Id); Assert.AreEqual(0, (await Store.FilesAsync("")).Count); Assert.AreEqual("keep", File.ReadAllText(path));
    }
    [TestMethod]
    public async Task BlacklistCrudAndInvalidInputPreserveExistingRecords()
    {
        var item = new BlacklistEntry { PlatformName = "平台", Author = "作者", WorksTime = new DateTime(2026, 10, 30), Remarks = "initial" };
        await Store.SaveBlacklistAsync(item); Assert.IsTrue(item.Id > 0);
        var stored = (await Store.BlacklistAsync("作者")).Single(); stored.Reason = "更新";
        await Store.SaveBlacklistAsync(stored); Assert.AreEqual("更新", (await Store.BlacklistAsync("平台")).Single().Reason);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Store.SaveBlacklistAsync(new BlacklistEntry { PlatformName = "" }));
        Assert.AreEqual(1, (await Store.BlacklistAsync("")).Count);
        await Store.DeleteBlacklistAsync(stored.Id); Assert.AreEqual(0, (await Store.BlacklistAsync("")).Count);
    }
}
