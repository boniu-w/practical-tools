using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.util;

namespace PracticalTools.Sqlite;

public class MyDbContext : DbContext
{
    public DbSet<DuplicateFilesEntry> DuplicateFiles { get; set; }

    public DbSet<Blacklist.Model.BlacklistEntry> Blacklist { get; set; }

    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

    // /// <summary>
    // /// sqlite
    // /// </summary>
    // /// <param name="optionsBuilder"></param>
    // protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    // {
    //     optionsBuilder.UseSqlite("Data Source=sqlite-PracticalTools.db");
    // }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 获取预定义的 DateTime 转换器
        ValueConverter dateTimeConverter = DateTimeConverter.Converter;

        // 遍历所有实体
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // 遍历实体的所有属性
            foreach (var property in entityType.GetProperties())
            {
                // 匹配属性名称和类型
                if (property.ClrType == typeof(DateTime?))
                {
                    // 应用转换器
                    property.SetValueConverter(dateTimeConverter);
                }
            }
        }
    }
}