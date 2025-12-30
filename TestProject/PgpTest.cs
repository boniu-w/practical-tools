using PracticalTools.GPG;

namespace TestProject1;

[TestClass]
public class PgpTest
{
    [TestMethod]
    public void TestPgp()
    {
        string identity = "boniu-w@live.cn";
        string passphrase = "Soccer0boy$";
        int keySize = 4096;
        var (pubRing, secRing) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, keySize);

        string pubKeyAscii = GpgHelper.ExportPublicKey(pubRing);
        string secKeyAscii = GpgHelper.ExportPrivateKey(secRing);
        Console.WriteLine(pubKeyAscii);
        Console.WriteLine(secKeyAscii);

        File.WriteAllText("pub-20251230.asc", pubKeyAscii);
        File.WriteAllText("sec-20251230.asc", secKeyAscii);

        #region MyRegion

        // 2. 待签数据
        string message = "Hello, this is a test message.";
        
        // 3. 生成签名
        string signature = GpgHelper.SignData(message, secRing, passphrase);
        Console.WriteLine("签名:\n" + signature);
        
        // 4. 验证签名
        bool isValid = GpgHelper.VerifySignature(message, signature, pubRing);
        Console.WriteLine("签名验证结果: " + isValid);

        #endregion
    }

    [TestMethod]
    public void TestSignPgp()
    {
        string identity = "boniu-w@live.cn";
        string passphrase = "Soccer0boy$";
        int keySize = 2048;
        var (pubRing, secRing) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, keySize);
        
        // 文件签名
        string filePath = "H:\\wg\\gpg-key\\alges - bala gaxeh.MP3";
        byte[] sig = GpgHelper.SignFile(filePath, secRing, passphrase);
        File.WriteAllBytes("app.exe.sig", sig);
        string sigPath = "app.exe.sig";

        bool a = GpgHelper.VerifyFileSignature(filePath, sigPath, pubRing);
        Console.WriteLine("文件签名验证结果: " + a);
    }
}