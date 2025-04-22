using PracticalTools.util;

namespace TestProject1;

[TestClass]
public class UnitTest1
{
    [TestMethod]
    public void TestMethod1()
    {
        string filePath = "H:\\aliyunpan\\movie\\《看见恶魔》强烈推荐! 片如其名 结尾巨加分 (2010).mp4";
        byte[] hash = FileUtil.ComputeFileHash(filePath);
        Console.WriteLine(BitConverter.ToString(hash).Replace("-", ""));
    }
}