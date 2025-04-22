using PracticalTools.util;

namespace TestProject1;

[TestClass]
public class UnitTest1
{
    [TestMethod]
    public void TestMethod1()
    {
        string filePath = "E:\\WXWork\\1688850903117122\\Cache\\File\\2025-04\\fa_20250416.sql";
        byte[] hash = FileUtil.ComputeFileHashMD5(filePath);
        Console.WriteLine(BitConverter.ToString(hash).Replace("-", ""));
    }

    [TestMethod]
    public void test()
    {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sqlite", "sqlite-PracticalTools.db");
        Console.WriteLine(dbPath);
    }
}