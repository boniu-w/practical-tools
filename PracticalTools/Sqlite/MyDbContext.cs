using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.util;

namespace PracticalTools.Sqlite;

public class MyDbContext: DbContext
{
    /// <summary>
    /// sqlite
    /// </summary>
    /// <param name="optionsBuilder"></param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=sqlite-PracticalTools.db");
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // modelBuilder.Entity<TheFile>(entity =>
        // {
        //     entity.Property(e => e.FileCreateTime)
        //         .HasConversion(
        //             v => v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
        //             v => v != null ? DateTime.Parse(v) : null);
        // });
        
        // modelBuilder.Entity<Blacklist.Model.Blacklist>(entity =>
        // {
        //     entity.Property(e => e.CreateTime)
        //         .HasConversion(
        //             v => v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
        //             v => v != null ? DateTime.Parse(v) : null);
        // });
        //
        // modelBuilder.Entity<Blacklist.Model.Blacklist>(entity =>
        // {
        //     entity.Property(e => e.UpdateTime)
        //         .HasConversion(
        //             v => v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
        //             v => v != null ? DateTime.Parse(v) : null);
        // });
        
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

    public DbSet<TheFile> DuplicateFiles{ get; set; }
    
    public DbSet<Blacklist.Model.Blacklist> Blacklist { get; set; }
}