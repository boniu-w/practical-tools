using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PracticalTools.Constant;
using PracticalTools.Sqlite;
using PracticalTools.Security;

namespace PracticalTools.Storage;

public static class DatabaseInitializer
{
    public static void InitializeProtectedStore(MyDbContext database, LocalAesSecretProtector protector)
    {
        Initialize(database);
        protector.InitializeKey(allowCreate: !database.GpgFile.Any());
    }

    public static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(path), Password = CommonConst.DbPassword
    }.ToString();

    public static void Initialize(MyDbContext database)
    {
        database.Database.EnsureCreated();
        // EnsureCreated does not add missing tables to an existing database.
        database.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ActivationRecord (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ActivationCode TEXT NOT NULL,
                ActivateTime TEXT,
                CreateTime TEXT
            );
            """);
    }
}
