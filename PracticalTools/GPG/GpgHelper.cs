using System.Diagnostics;
using System.IO;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.IO;

namespace PracticalTools.GPG;

public class GpgHelper
{
    private const string GpgPath = @"C:\Program Files (x86)\gnupg\bin\gpg.exe";

    // --batch：非交互模式，遇到问题报错，不弹窗
    // --yes：自动回答“yes”给信任提示等问题
    public static void EncryptFile1(string inputFile, string outputFile, string recipient)
    {
        var args = $"--batch --yes --output \"{outputFile}\" --encrypt --recipient \"{recipient}\" \"{inputFile}\"";
        RunGpgCommand(args);
    }

    public static void DecryptFile(string inputFile, string outputFile)
    {
        var args = $"--output \"{outputFile}\" --decrypt \"{inputFile}\"";
        RunGpgCommand(args);
    }

    private static void RunGpgCommand(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GpgPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"GPG error: {error}");
        }
    }

    /// <summary>
    /// 读取公钥
    /// </summary>
    /// <param name="publicKeyStream"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static PgpPublicKey ReadPublicKey(Stream publicKeyStream)
    {
        using var input = PgpUtilities.GetDecoderStream(publicKeyStream);
        var bundle = new PgpPublicKeyRingBundle(input);

        foreach (PgpPublicKeyRing ring in bundle.GetKeyRings())
        {
            foreach (PgpPublicKey key in ring.GetPublicKeys())
            {
                if (key.IsEncryptionKey)
                    return key;
            }
        }

        throw new ArgumentException("未找到可用于加密的公钥");
    }

    /// <summary>
    /// 读取私钥
    /// </summary>
    /// <param name="privateKeyStream"></param>
    /// <param name="passphrase"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static PgpPrivateKey ReadPrivateKey(
        Stream privateKeyStream,
        string passphrase)
    {
        using var input = PgpUtilities.GetDecoderStream(privateKeyStream);
        var bundle = new PgpSecretKeyRingBundle(input);

        foreach (PgpSecretKeyRing ring in bundle.GetKeyRings())
        {
            foreach (PgpSecretKey key in ring.GetSecretKeys())
            {
                if (key.IsSigningKey || key.IsMasterKey)
                {
                    return key.ExtractPrivateKey(passphrase.ToCharArray());
                }
            }
        }

        throw new ArgumentException("未找到私钥");
    }

    /// <summary>
    /// 加密
    /// </summary>
    /// <param name="inputFile"></param>
    /// <param name="outputFile"></param>
    /// <param name="publicKeyFile"></param>
    public static void EncryptFile(string inputFile, string outputFile, string publicKeyFile)
    {
        using var pubKeyStream = File.OpenRead(publicKeyFile);
        var pubKey = ReadPublicKey(pubKeyStream);

        using var outStream = File.Create(outputFile);

        var encGen = new PgpEncryptedDataGenerator(
            SymmetricKeyAlgorithmTag.Aes256,
            true,
            new SecureRandom());

        encGen.AddMethod(pubKey);

        using var encOut = encGen.Open(outStream, new byte[1 << 16]);

        var literalGen = new PgpLiteralDataGenerator();
        using var literalOut = literalGen.Open(
            encOut,
            PgpLiteralData.Binary,
            Path.GetFileName(inputFile),
            new FileInfo(inputFile).Length,
            DateTime.UtcNow);

        using var input = File.OpenRead(inputFile);
        Streams.PipeAll(input, literalOut);
    }

    /// <summary>
    /// 解密
    /// </summary>
    /// <param name="inputFile"></param>
    /// <param name="outputFile"></param>
    /// <param name="privateKeyFile"></param>
    /// <param name="passphrase"></param>
    /// <exception cref="Exception"></exception>
    public static void DecryptFile(string inputFile, string outputFile, string privateKeyFile, string passphrase)
    {
        using var input = File.OpenRead(inputFile);
        using var decoder = PgpUtilities.GetDecoderStream(input);
        var pgpObjFactory = new PgpObjectFactory(decoder);

        var encDataList = pgpObjFactory.NextPgpObject() as PgpEncryptedDataList
                          ?? (PgpEncryptedDataList)pgpObjFactory.NextPgpObject();

        using var privKeyStream = File.OpenRead(privateKeyFile);
        var privKey = ReadPrivateKey(privKeyStream, passphrase);

        PgpPublicKeyEncryptedData encData = null;

        foreach (PgpPublicKeyEncryptedData data in encDataList.GetEncryptedDataObjects())
        {
            if (data.KeyId == privKey.KeyId)
            {
                encData = data;
                break;
            }
        }

        if (encData == null)
            throw new Exception("找不到匹配的私钥");

        using var clear = encData.GetDataStream(privKey);
        var plainFact = new PgpObjectFactory(clear);
        var message = plainFact.NextPgpObject();

        if (message is PgpLiteralData literal)
        {
            using var outStream = File.Create(outputFile);
            Streams.PipeAll(literal.GetInputStream(), outStream);
        }
    }

    /// <summary>
    /// 签名
    /// </summary>
    /// <param name="inputFile"></param>
    /// <param name="outputSigFile"></param>
    /// <param name="privateKeyFile"></param>
    /// <param name="passphrase"></param>
    public static void SignFile(
        string inputFile,
        string outputSigFile,
        string privateKeyFile,
        string passphrase)
    {
        using var keyIn = File.OpenRead(privateKeyFile);
        var privateKey = ReadPrivateKey(keyIn, passphrase);

        var signer = new PgpSignatureGenerator(privateKey.PublicKeyPacket.Algorithm, HashAlgorithmTag.Sha256);

        signer.InitSign(PgpSignature.BinaryDocument, privateKey);

        using var outStream = File.Create(outputSigFile);
        using var input = File.OpenRead(inputFile);

        int ch;
        while ((ch = input.ReadByte()) >= 0)
            signer.Update((byte)ch);

        signer.Generate().Encode(outStream);
    }

    /// <summary>
    /// 验证签名
    /// </summary>
    /// <param name="inputFile"></param>
    /// <param name="sigFile"></param>
    /// <param name="publicKeyFile"></param>
    /// <returns></returns>
    public static bool VerifySignature(string inputFile, string sigFile, string publicKeyFile)
    {
        // 1. 读取公钥
        using var pubKeyStream = File.OpenRead(publicKeyFile);
        var pubKey = ReadPublicKey(pubKeyStream);

        // 2. 读取签名文件
        using var sigInput = PgpUtilities.GetDecoderStream(File.OpenRead(sigFile));
        var pgpFact = new PgpObjectFactory(sigInput);

        PgpSignature signature = null;
        PgpObject pgpObj = pgpFact.NextPgpObject();

        // 3. 解压（如果有）
        if (pgpObj is PgpCompressedData compressed)
        {
            using var compStream = compressed.GetDataStream();
            var compFact = new PgpObjectFactory(compStream);
            pgpObj = compFact.NextPgpObject();
        }

        // 4. 取 SignatureList
        if (pgpObj is PgpSignatureList sigList)
        {
            signature = sigList[0];
        }
        else
        {
            throw new Exception("签名格式不正确");
        }

        // 5. 初始化验签
        signature.InitVerify(pubKey);

        // 6. 读原文件并更新签名
        using var input = File.OpenRead(inputFile);
        int ch;
        while ((ch = input.ReadByte()) >= 0)
        {
            signature.Update((byte)ch);
        }

        // 7. 验证
        return signature.Verify();
    }
}