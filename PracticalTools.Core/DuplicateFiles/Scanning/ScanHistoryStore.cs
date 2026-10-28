using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.Sqlite;

namespace PracticalTools.DuplicateFiles.Scanning;

public sealed class ScanHistoryStore(IServiceScopeFactory scopeFactory)
{
    public Task SaveAsync(DuplicateScan scan) => Task.Run(() =>
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        var run = Guid.NewGuid().ToString("N");
        db.DuplicateFiles.AddRange(scan.Files.Select((f, i) => new DuplicateFilesEntry
        {
            Id = $"{run}:{i}", FileName = f.Name, FilePath = f.FilePath, FileSize = Math.Round(f.Length / 1024d, 3),
            FileCreateTime = f.CreationUtc, FileLastedModifiedTime = f.LastWriteUtc, ScanDirectory = f.Root,
            ScanTime = scan.CompletedUtc, Md5Value = f.Md5
        }));
        db.SaveChanges();
    });

    public Task<List<DuplicateFilesEntry>> RecentAsync() => Task.Run(() =>
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MyDbContext>().DuplicateFiles.AsNoTracking()
            .OrderByDescending(f => f.ScanTime).ThenBy(f => f.FilePath).Take(500).ToList();
    });
}
