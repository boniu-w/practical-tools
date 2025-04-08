using Microsoft.EntityFrameworkCore;
using PracticalTools.DuplicateFiles.Model;

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
        modelBuilder.Entity<TheFile>(entity =>
        {
            entity.Property(e => e.FileCreateTime)
                .HasConversion(
                    v => v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
                    v => v != null ? DateTime.Parse(v) : null);
        });
    }

    public DbSet<TheFile> DuplicateFiles{ get; set; }
}