using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.FileManager.Model;
using PracticalTools.GPG;
using PracticalTools.Activation.Model;
using PracticalTools.util;
using PracticalTools.Security;

namespace PracticalTools.Sqlite;

public class MyDbContext : DbContext
{
    internal ISecretProtector SecretProtector { get; }
    public DbSet<DuplicateFilesEntry> DuplicateFiles { get; set; }

    public DbSet<BlacklistEntry> Blacklist { get; set; }
    
    public DbSet<MyFile> MyFile { get; set; }
    
    public DbSet<GpgFile> GpgFile { get; set; }

    public DbSet<ActivationRecord> ActivationRecord { get; set; }

    public MyDbContext(DbContextOptions<MyDbContext> options, ISecretProtector? secretProtector = null)
        : base(options)
    {
        SecretProtector = secretProtector ?? LegacyDpapiSecretProtector.Instance;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, SecretProtectionModelCacheKeyFactory>();

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

        // WPF keeps DPAPI by default; Avalonia explicitly injects its versioned local protector.
        var protector = SecretProtector;
        modelBuilder.Entity<GpgFile>()
            .Property(e => e.Passphrase)
            .HasConversion(
                v => protector.Protect(v),
                v => protector.Unprotect(v));
    }
}
