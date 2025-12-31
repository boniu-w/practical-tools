using Org.BouncyCastle.Bcpg.OpenPgp;
using PracticalTools.GPG;
using PracticalTools.util;

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
        long validDuration = long.MaxValue;
        var (pubRing, secRing) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, keySize);

        string pubKeyAscii = GpgHelper.ExportPublicKey(pubRing);
        string secKeyAscii = GpgHelper.ExportPrivateKey(secRing);
        Console.WriteLine(pubKeyAscii);
        Console.WriteLine(secKeyAscii);

        // File.WriteAllText("pub-20251230.asc", pubKeyAscii);
        // File.WriteAllText("sec-20251230.asc", secKeyAscii);

        GpgFile gpgFile = GenerateGpgFile(identity, passphrase, keySize, validDuration, pubKeyAscii, secKeyAscii);
        Console.WriteLine(gpgFile.ToString());

        #region 对信息签名

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

    private static GpgFile GenerateGpgFile(
        string identity,
        string passphrase,
        int keySize,
        long validDuration = 0,
        string pubKey = "",
        string secretKey = "",
        string signaturePub = "",
        string signatureSec = "",
        string md5Pub = "",
        string md5Sec = ""
    )
    {
        GpgFile gpgFile = new GpgFile();
        long id = new SnowflakeId(1, 1).NextId();
        DateTime now = DateTime.Now;
        gpgFile.CreateTime = now;
        gpgFile.UpdateTime = now;
        gpgFile.Type = ".asc";
        gpgFile.Id = id;
        gpgFile.SignaturePublic = signaturePub;
        gpgFile.SignaturePrivate = signatureSec;
        gpgFile.FileNamePublic = string.Concat("pub-", now.Millisecond, ".asc");
        gpgFile.FileNamePrivate = string.Concat("sec-", now.Millisecond, ".asc");
        gpgFile.Md5ValuePublic = md5Pub;
        gpgFile.Md5ValuePrivate = md5Sec;
        gpgFile.FileCreateTime = now;
        gpgFile.ValidDurationSeconds = validDuration;
        gpgFile.SecretKey = secretKey;
        gpgFile.PublicKey = pubKey;
        gpgFile.Identity = identity;
        gpgFile.Passphrase = passphrase;
        gpgFile.KeySize = keySize;

        return gpgFile;
    }

    // [TestMethod]
    public void TestSignPgp()
    {
        // string identity = "boniu-w@live.cn";
        // string passphrase = "Soccer0boy$";
        // int keySize = 2048;
        // var (pubRing, secRing) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, keySize);

        // 文件签名
        string secFile = "";
        var secRing = GpgHelper.LoadSecretKeyRing(secFile);
        string pubFile = "";
        PgpPublicKeyRing pubRing = GpgHelper.LoadPublicKeyRing(pubFile);

        string passphrase = "Soccer0boy$";
        string filePath = "H:\\wg\\gpg-key\\alges - bala gaxeh.MP3";
        byte[] sig = GpgHelper.SignFile(filePath, secRing, passphrase);
        File.WriteAllBytes("app.exe.sig", sig);
        string sigPath = "app.exe.sig";

        bool a = GpgHelper.VerifyFileSignature(filePath, sigPath, pubRing);
        Console.WriteLine("文件签名验证结果: " + a);
    }

    // [TestMethod]
    public void TestSignPgpFile()
    {
        // 文件签名
        string secFile = "D:\\c-project\\wg\\temp\\sec-20251230.asc";
        PgpSecretKeyRing secRing = GpgHelper.LoadSecretKeyRing(secFile);
        string pubFile = "D:\\c-project\\wg\\temp\\pub-20251230.asc";
        PgpPublicKeyRing pubRing = GpgHelper.LoadPublicKeyRing(pubFile);

        string passphrase = "Soccer0boy$";
        string filePath = "H:\\wg\\gpg-key\\alges - bala gaxeh.MP3";
        // string signatureFilePath = GpgHelper.SignatureFile(filePath, secRing, passphrase);
        string signatureFilePath = GpgHelper.Test12(filePath, secRing, passphrase);
        Console.WriteLine("<signatureFilePath>: " + signatureFilePath);

        bool a = GpgHelper.VerifyFileSignatureSafe(filePath, signatureFilePath, pubRing);
        Console.WriteLine("文件签名验证结果: " + a);
    }
}