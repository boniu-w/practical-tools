using PracticalTools.util;

namespace TestProject1;

[TestClass]
public class FileUnitTest
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

        // List<string> extensions = FileUtil.GetAllFileExtensions(filePath);
        // foreach (string extension in extensions)
        // {
        //     Console.WriteLine(extension);
        // }
        //
        List<string> allFiles = FileUtil.GetAllFiles(filePath, fileList: null, allowedExtensions: new[] { ".txt", ".jpg", ".md" });
        // List<string> allFiles = FileUtil.GetAllFiles(filePath, fileList: null, allowedExtensions: null);
        foreach (string file in allFiles)
        {
            Console.WriteLine(file);
        }

        // List<(string, long, DateTime)> tuples = FileUtil.GetAllFiles(filePath);
        // foreach ((string, long, DateTime) tuple in tuples)
        // {
        //     var file = tuple.Item1;
        //     long fileSize = tuple.Item2;
        //     DateTime lastWriteTime = tuple.Item3;
        //     Console.WriteLine(fileSize);
        // }
    }

    [TestMethod]
    public void test()
    {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sqlite", "sqlite-PracticalTools.db");
        Console.WriteLine(dbPath);
    }
}