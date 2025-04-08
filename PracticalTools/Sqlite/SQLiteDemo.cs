using Microsoft.Data.Sqlite;
using PracticalTools.DuplicateFiles.Model;

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

    /// <summary>
    /// Microsoft.EntityFrameworkCore 框架
    /// </summary>
    public void Insert(TheFile theFile)
    {
        theFile = new TheFile();
        theFile.Id = Guid.NewGuid().ToString();
        theFile.FileName = "filename";
        theFile.FilePath = "filepath";
        theFile.FileSize = 11;
        theFile.FileCreateTime = DateTime.Now;

        using (var myDbContext = new MyDbContext())
        {
            myDbContext.Database.EnsureCreated(); // 确保数据库存在
            myDbContext.DuplicateFiles.Add(theFile);
            myDbContext.SaveChanges();
        }
    }

    /// <summary>
    /// 原生
    /// </summary>
    public void Select()
    {
        using (var command = SqliteConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM DuplicateFiles";
            using var reader = command.ExecuteReader();
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
    public void List()
    {
        var myDbContext = new MyDbContext();
        var duplicateFiles = myDbContext.DuplicateFiles;
        foreach (TheFile theFile in duplicateFiles)
        {
            Console.WriteLine(theFile);
        }
    }
}