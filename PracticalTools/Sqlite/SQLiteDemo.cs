using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.util;
using PracticalTools.vo;

namespace PracticalTools.Sqlite;

public class SQLiteDemo
{
    /// <summary>
    /// 原生
    /// </summary>
    public SqliteConnection SqliteConnection { get; set; }

    /// <summary>
    /// 原生
    /// </summary>
    public void Open()
    {
        string dbPath = "sqlite-PracticalTools.db"; // 数据库文件路径
        SqliteConnection = new SqliteConnection($"Data Source={dbPath}");
        SqliteConnection.Open();
    }

    /// <summary>
    /// 原生
    /// </summary>
    public void Close()
    {
        SqliteConnection.Close();
    }

    /// <summary>
    /// 原生
    /// </summary>
    public void CreateTable()
    {
        using (var command = SqliteConnection.CreateCommand())
        {
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS DuplicateFiles (
                                        Id TEXT PRIMARY KEY ,
                                        FileName TEXT NOT NULL,
                                        FilePath TEXT ,
                                        FileSize Real,
                                        FileCreateTime TEXT
                                    )
                                  """;
            command.ExecuteNonQuery();
        }
    }

    public void CreateTableBlackList()
    {
        using (var command = SqliteConnection.CreateCommand())
        {
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS Blacklist (
                                        Id INTEGER PRIMARY KEY ,
                                        PlatformName TEXT,
                                        Author TEXT ,
                                        Product TEXT,
                                        WorksTime TEXT,
                                        Reason TEXT,
                                        WorksType TEXT,
                                        Remarks TEXT,
                                        CreateTime TEXT,
                                        UpdateTime TEXT,
                                        CreateUser TEXT,
                                        UpdateUser TEXT
                                    )
                                  """;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Microsoft.EntityFrameworkCore 框架
    /// </summary>
    // public void InsertTheFile(TheFile theFile)
    // {
    //     theFile = new TheFile();
    //     theFile.Id = Guid.NewGuid().ToString();
    //     theFile.FileName = "filename";
    //     theFile.FilePath = "filepath";
    //     theFile.FileSize = 11;
    //     theFile.FileCreateTime = DateTime.Now;
    //
    //     using (var myDbContext = new MyDbContext())
    //     {
    //         myDbContext.Database.EnsureCreated(); // 确保数据库存在
    //         myDbContext.DuplicateFiles.Add(theFile);
    //         myDbContext.SaveChanges();
    //     }
    // }
    MyDbContext _myDbContext = ((App)Application.Current)._serviceProvider.GetRequiredService<MyDbContext>();

    public void InsertBlackList()
    {
        Blacklist.Model.BlacklistEntry blacklist = new Blacklist.Model.BlacklistEntry()
        {
            Id = new SnowflakeIdGenerator(1, 1).NextId(),
            PlatformName = "淘宝",
            Author = "张三",
            Product = "茶杯",
            Reason = "质量差",
            WorksTime = DateTime.Now,
            CreateTime = DateTime.Now,
            UpdateTime = DateTime.Now,
            CreateUser = "王刚",
            UpdateUser = "王刚"
        };

        // using (var myDbContext = new MyDbContext(options => 
        //            options.UseSqlite(_configuration.GetConnectionString("DefaultConnection"))))
        // {
        // myDbContext.Database.EnsureCreated(); // 确保数据库存在
        // myDbContext.Blacklist.Add(blacklist);
        // myDbContext.SaveChangesAsync();
        // myDbContext.SaveChanges();
        // }

        _myDbContext.Database.EnsureCreated(); // 确保数据库存在
        _myDbContext.Blacklist.Add(blacklist);
        _myDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 原生
    /// </summary>
    public void Select()
    {
        using (SqliteCommand command = SqliteConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM DuplicateFiles";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                string name = reader.GetString(1);
                Console.WriteLine($"ID: {id}, Name: {name}");
            }
        }
    }

    /// <summary>
    /// Microsoft.EntityFrameworkCore 框架
    /// </summary>
    // public List<TheFile> List()
    // {
    //     MyDbContext myDbContext = new MyDbContext();
    //     DbSet<TheFile> duplicateFiles = myDbContext.DuplicateFiles;
    //     if (duplicateFiles.Any())
    //     {
    //         foreach (TheFile theFile in duplicateFiles)
    //         {
    //             Console.WriteLine(theFile);
    //         }
    //
    //         return duplicateFiles.ToList();
    //     }
    //
    //     return null;
    // }
}