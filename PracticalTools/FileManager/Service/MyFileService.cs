using Microsoft.EntityFrameworkCore.ChangeTracking;
using PracticalTools.FileManager.Model;
using PracticalTools.Sqlite;
using PracticalTools.util;

namespace PracticalTools.FileManager.Service;

public class MyFileService
{
    public List<MyFile> List(MyDbContext dbContext)
    {
        return null;
    }

    public void Add(MyFile file, MyDbContext dbContext)
    {
        file.Id = new SnowflakeId(1, 1).NextId().ToString();

        file.CreateTime = DateTime.Now;
        file.UpdateTime = DateTime.Now;
        EntityEntry<MyFile> entry = dbContext.MyFile.Add(file);
        dbContext.SaveChanges();
    }
}