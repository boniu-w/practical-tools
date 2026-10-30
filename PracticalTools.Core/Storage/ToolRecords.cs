using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.Blacklist.Model;
using PracticalTools.FileManager.Model;
using PracticalTools.Sqlite;

namespace PracticalTools.Storage;

public sealed class ToolRecords(IServiceScopeFactory scopes)
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    public Task<List<MyFile>> FilesAsync(string search) => Read(db => db.MyFile.AsNoTracking()
        .Where(f => f.FileName.Contains(search) || (f.FilePath != null && f.FilePath.Contains(search)))
        .OrderBy(f => f.FileName).ThenBy(f => f.Id).Take(1000).ToList());
    public Task<List<BlacklistEntry>> BlacklistAsync(string search) => Read(db => db.Blacklist.AsNoTracking()
        .Where(f => (f.Author != null && f.Author.Contains(search)) || (f.PlatformName != null && f.PlatformName.Contains(search)) ||
            (f.Product != null && f.Product.Contains(search)))
        .OrderByDescending(f => f.Id).Take(1000).ToList());
    private Task<T> Read<T>(Func<MyDbContext, T> action) => Task.Run(() =>
    { using var scope = scopes.CreateScope(); return action(scope.ServiceProvider.GetRequiredService<MyDbContext>()); });
    private async Task Write(Action<MyDbContext> action)
    {
        await _writes.WaitAsync();
        try { await Read(db => { action(db); db.SaveChanges(); return true; }); }
        finally { _writes.Release(); }
    }
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var records = await Task.Run(() => paths.Distinct().Select(path =>
        {
            path = Path.GetFullPath(path); LocalFileAccess.NoLinks(path);
            var f = new FileInfo(path); long length = f.Length; var created = f.CreationTimeUtc; var modified = f.LastWriteTimeUtc;
            using var stream = LocalFileAccess.Open(path);
            if (!stream.CanSeek) throw new IOException("只支持普通文件。");
            var md5 = Convert.ToHexString(MD5.HashData(stream));
            LocalFileAccess.CheckSnapshot(path, Path.GetDirectoryName(path)!, length, created, modified);
            return new MyFile { Id = Guid.NewGuid().ToString("N"), FileName = f.Name, FilePath = path, FileSize = length / 1024d,
                FileCreateTime = created, FileLastedModifiedTime = modified, Md5Value = md5, Type = f.Extension, CreateTime = DateTime.Now, UpdateTime = DateTime.Now };
        }).Take(1001).ToList());
        if (records.Count > 1000) throw new ArgumentException("单次最多添加 1000 个文件。");
        await Write(db =>
        {
            var existing = db.MyFile.Select(f => f.FilePath).ToList().Where(p => !string.IsNullOrWhiteSpace(p))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var f in records)
                if (existing.Add(f.FilePath)) db.MyFile.Add(f);
        });
    }
    public Task UpdateFileAsync(string id, string type) => Write(db =>
    {
        if (type.Length > 255) throw new ArgumentException("类别最多 255 字符。");
        var f = db.MyFile.Single(x => x.Id == id); f.Type = type.Trim(); f.UpdateTime = DateTime.Now;
    });
    public Task DeleteFileAsync(string id) => Write(db => { var f = db.MyFile.Find(id); if (f != null) db.MyFile.Remove(f); });
    public Task SaveBlacklistAsync(BlacklistEntry value) => Write(db =>
    {
        if (string.IsNullOrWhiteSpace(value.PlatformName) || string.IsNullOrWhiteSpace(value.Author)) throw new ArgumentException("平台和作者不能为空。");
        Validator.ValidateObject(value, new ValidationContext(value), true);
        if (value.Id == 0) { value.CreateTime = DateTime.Now; value.UpdateTime = DateTime.Now; db.Blacklist.Add(value); }
        else
        {
            var stored = db.Blacklist.Single(x => x.Id == value.Id);
            stored.PlatformName = value.PlatformName; stored.Author = value.Author; stored.Product = value.Product;
            stored.WorksTime = value.WorksTime; stored.Reason = value.Reason; stored.WorksType = value.WorksType;
            stored.Remarks = value.Remarks; stored.UnifiedSocialCreditIdentifier = value.UnifiedSocialCreditIdentifier;
            stored.UpdateTime = DateTime.Now;
        }
    });
    public Task DeleteBlacklistAsync(long id) => Write(db => { var f = db.Blacklist.Find(id); if (f != null) db.Blacklist.Remove(f); });
}
