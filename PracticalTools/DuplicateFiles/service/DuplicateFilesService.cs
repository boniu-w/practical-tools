using PracticalTools.DuplicateFiles.Model;
using PracticalTools.Sqlite;

namespace PracticalTools.DuplicateFiles.service;

public class DuplicateFilesService
{
    public List<DuplicateFilesEntry>? DuplicateFilesList { get; set; }

    private readonly MyDbContext _myDbContext;

    public DuplicateFilesService(MyDbContext myDbContext)
    {
        _myDbContext = myDbContext;
    }

    public List<DuplicateFilesEntry>? List()
    {
        return _myDbContext.DuplicateFiles.ToList();
    }

    public int Add(DuplicateFilesEntry duplicateFilesEntry)
    {
        _myDbContext.Database.EnsureCreated(); // 确保数据库存在
        _myDbContext.DuplicateFiles.Add(duplicateFilesEntry);
        _myDbContext.SaveChanges();

        return 1;
    }

    public int Update(DuplicateFilesEntry duplicateFilesEntry)
    {
        _myDbContext.Database.EnsureCreated(); // 确保数据库存在
        _myDbContext.DuplicateFiles.Update(duplicateFilesEntry);
        _myDbContext.SaveChanges();
        return 1;
    }
}