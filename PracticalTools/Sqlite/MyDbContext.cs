using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.FileManager.Model;
using PracticalTools.GPG;
using PracticalTools.util;

namespace PracticalTools.Sqlite;

public class MyDbContext : DbContext
{
    public DbSet<DuplicateFilesEntry> DuplicateFiles { get; set; }

    public DbSet<BlacklistEntry> Blacklist { get; set; }
    
    public DbSet<MyFile> MyFile { get; set; }
    
    public DbSet<GpgFile> GpgFile { get; set; }

    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

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

        // GpgFile.Passphrase 使用 DPAPI 加密存储
        modelBuilder.Entity<GpgFile>()
            .Property(e => e.Passphrase)
            .HasConversion(
                v => CryptoHelper.Encrypt(v),  // 写入 DB 前加密
                v => CryptoHelper.Decrypt(v));  // 从 DB 读取后解密
    }
}