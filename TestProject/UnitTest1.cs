using PracticalTools.util;

namespace TestProject1;

[TestClass]
public class UnitTest1
{
    // 26秒, 文件大小 3G
    [TestMethod]
    public void TestFileUtilHash()
    {
        // 文件大小 3G
        string filePath = "H:\\movie\\低俗小说\\Pulp.Fiction.1994.1080p.BluRay.H264.AAC-RARBG\\Pulp.Fiction.1994.1080p.BluRay.H264.AAC-RARBG.mp4";
        byte[] hash = FileUtil.ComputeFileHashMD5(filePath);
        Console.WriteLine(BitConverter.ToString(hash).Replace("-", ""));
    }

    // 26秒, 文件大小 3G
    [TestMethod]
    public void TestFileUtilHash1()
    {
        // 文件大小 3G
        string filePath = "H:\\movie\\低俗小说\\Pulp.Fiction.1994.1080p.BluRay.H264.AAC-RARBG\\Pulp.Fiction.1994.1080p.BluRay.H264.AAC-RARBG.mp4";

        byte[] bytes = FileUtil.GetFileBytes(new FileInfo(filePath), "sha1");
        Console.WriteLine(BitConverter.ToString(bytes).Replace("-", ""));
    }

    [TestMethod]
    public void TestFileUtilHash2()
    {
        // 文件大小 3G
        string filePath = "H:\\movie\\低俗小说\\Pulp.Fiction.1994.1080p.BluRay.H264.AAC-RARBG\\Pulp.Fiction.1994.1080p.BluRay.H264.AAC-RARBG.mp4";

        byte[] bytes = FileUtil.GetFileHashWithBuffer(filePath, "sha1");
        Console.WriteLine(BitConverter.ToString(bytes).Replace("-", ""));
    }

    [TestMethod]
    public void TestGetAllFile()
    {
        string filePath = "H:\\git使用";

        List<string> allFiles = FileUtil.GetAllFiles(filePath, fileList: new List<string>());
        foreach (string file in allFiles)
        {
            Console.WriteLine(file);
        }
    }

    [TestMethod]
    public void TestGetAllFileSome()
    {
        string filePath = "H:\\git使用";

        List<string> allFiles = FileUtil.GetAllFiles(filePath, fileList: null, allowedExtensions: new[] { ".txt", ".jpg", ".md" });
        foreach (string file in allFiles)
        {
            Console.WriteLine(file);
        }
    }

    [TestMethod]
    public void test()
    {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sqlite", "sqlite-PracticalTools.db");
        Console.WriteLine(dbPath);
    }
}