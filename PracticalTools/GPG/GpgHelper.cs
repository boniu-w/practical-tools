using System.Diagnostics;
using System.IO;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg.Sig;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
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
    /// 生成签名
    /// </summary>
    /// <param name="data"></param>
    /// <param name="secretKeyRing"></param>
    /// <param name="passphrase"></param>
    /// <returns></returns>
    public static string SignData(string data, PgpSecretKeyRing secretKeyRing, string passphrase)
    {
        // 1. 获取私钥对象（主密钥或子密钥都可）
        PgpSecretKey secretKey = secretKeyRing.GetSecretKey();
        PgpPrivateKey privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());

        // 2. 初始化签名生成器
        var signatureGenerator = new PgpSignatureGenerator(secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        signatureGenerator.InitSign(PgpSignature.BinaryDocument, privateKey);

        // 3. 可选：添加签名子包信息
        var subpacket = new PgpSignatureSubpacketGenerator();
        subpacket.SetSignerUserId(false, secretKey.PublicKey.GetUserIds().First());
        signatureGenerator.SetHashedSubpackets(subpacket.Generate());

        // 4. 输出到 ASCII-armored
        using var outputStream = new MemoryStream();
        using (var armoredStream = new ArmoredOutputStream(outputStream))
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            signatureGenerator.Update(bytes, 0, bytes.Length);
            signatureGenerator.Generate().Encode(armoredStream);
        }

        return Encoding.UTF8.GetString(outputStream.ToArray());
    }

    /// <summary>
    /// 验证签名
    /// </summary>
    /// <param name="data"></param>
    /// <param name="signatureAscii"></param>
    /// <param name="publicKeyRing"></param>
    /// <returns></returns>
    public static bool VerifySignature(string data, string signatureAscii, PgpPublicKeyRing publicKeyRing)
    {
        // 1. 解析签名
        using var sigStream = new MemoryStream(Encoding.UTF8.GetBytes(signatureAscii));
        var decoderStream = PgpUtilities.GetDecoderStream(sigStream);
        PgpObjectFactory pgpObjFactory = new PgpObjectFactory(decoderStream);
        PgpSignatureList sigList = null;

        var obj = pgpObjFactory.NextPgpObject();
        if (obj is PgpSignatureList sList)
            sigList = sList;
        else
            throw new Exception("Unexpected PGP object type");

        var signature = sigList[0];

        // 2. 找到对应公钥
        PgpPublicKey pubKey = publicKeyRing.GetPublicKey(signature.KeyId);
        signature.InitVerify(pubKey);

        // 3. 更新原始数据
        var bytes = Encoding.UTF8.GetBytes(data);
        signature.Update(bytes, 0, bytes.Length);

        // 4. 验证
        return signature.Verify();
    }

    /// <summary>
    /// 生成文件签名
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="secretKeyRing"></param>
    /// <param name="passphrase"></param>
    /// <returns></returns>
    public static byte[] SignFile(
        string exePath,
        PgpSecretKeyRing secretKeyRing,
        string passphrase)
    {
        // 1. 读取文件
        byte[] fileBytes = File.ReadAllBytes(exePath);

        // 2. 获取私钥
        PgpSecretKey secretKey = secretKeyRing.GetSecretKey();
        PgpPrivateKey privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());

        // 3. 初始化签名生成器
        var sigGen = new PgpSignatureGenerator(
            secretKey.PublicKey.Algorithm,
            HashAlgorithmTag.Sha256);

        sigGen.InitSign(PgpSignature.BinaryDocument, privateKey);

        // 4. 更新文件内容
        sigGen.Update(fileBytes, 0, fileBytes.Length);

        // 5. 生成签名
        using var output = new MemoryStream();
        sigGen.Generate().Encode(output);

        return output.ToArray();
    }

    public static string Test12(string exePath,
        PgpSecretKeyRing secretKeyRing,
        string passphrase)
    {
        byte[] sig = SignFile(exePath, secretKeyRing, passphrase);
        string sigFilePath = exePath + ".sig";
        File.WriteAllBytes(sigFilePath, sig);
        return sigFilePath;
    }

    public static string SignatureFile(string exePath,
        PgpSecretKeyRing secretKeyRing,
        string passphrase)
    {
        // 1. 读取文件
        byte[] fileBytes = File.ReadAllBytes(exePath);

        // 2. 获取私钥
        PgpSecretKey secretKey = secretKeyRing.GetSecretKey();
        PgpPrivateKey privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());

        // 3. 初始化签名生成器
        var sigGen = new PgpSignatureGenerator(secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        sigGen.InitSign(PgpSignature.BinaryDocument, privateKey);

        // 4. 更新文件内容
        sigGen.Update(fileBytes, 0, fileBytes.Length);

        // 5. 生成 ASCII-armored detached signature 并保存到磁盘
        string sigFilePath = exePath + ".sig";
        using (var sigOut = File.Create(sigFilePath))
        using (var armored = new ArmoredOutputStream(sigOut))
        {
            sigGen.Generate().Encode(armored);
        }

        return sigFilePath;
    }

    /// <summary>
    /// 验证文件签名
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="sigPath"></param>
    /// <param name="publicKeyRing"></param>
    /// <returns></returns>
    public static bool VerifyFileSignature(
        string exePath,
        string sigPath,
        PgpPublicKeyRing publicKeyRing)
    {
        byte[] fileBytes = File.ReadAllBytes(exePath);
        byte[] sigBytes = File.ReadAllBytes(sigPath);

        using var sigStream = new MemoryStream(sigBytes);
        PgpObjectFactory factory = new PgpObjectFactory(sigStream);
        PgpSignatureList sigList = (PgpSignatureList)factory.NextPgpObject();
        PgpSignature signature = sigList[0];

        PgpPublicKey pubKey = publicKeyRing.GetPublicKey(signature.KeyId);
        signature.InitVerify(pubKey);

        signature.Update(fileBytes, 0, fileBytes.Length);

        return signature.Verify();
    }

    /// <summary>
    /// 安全验证 PGP detached signature（兼容 PgpSignatureList 和 PgpOnePassSignatureList）
    /// </summary>
    /// <param name="exePath">原文件路径</param>
    /// <param name="sigPath">签名文件路径（ASCII-armored 或二进制）</param>
    /// <param name="publicKeyRing">公钥环</param>
    /// <returns>签名验证成功返回 true，否则 false</returns>
    public static bool VerifyFileSignatureSafe(
        string exePath,
        string sigPath,
        PgpPublicKeyRing publicKeyRing)
    {
        try
        {
            // 1. 读取原文件
            byte[] fileBytes = File.ReadAllBytes(exePath);

            // 2. 打开签名文件
            using var sigStream = File.OpenRead(sigPath);
            using var decoderStream = PgpUtilities.GetDecoderStream(sigStream);

            var pgpFactory = new PgpObjectFactory(decoderStream);
            PgpObject obj = pgpFactory.NextPgpObject();

            PgpSignature signature = null;

            // 3. PgpSignatureList（最常见 detached signature）
            if (obj is PgpSignatureList sigList && sigList.Count > 0)
            {
                signature = sigList[0];
            }
            // 4. PgpOnePassSignatureList（流式签名）
            else if (obj is PgpOnePassSignatureList onePassList && !onePassList.IsEmpty)
            {
                var onePass = onePassList[0];
                PgpPublicKey pubKey = publicKeyRing.GetPublicKey(onePass.KeyId);
                if (pubKey == null) return false;

                onePass.InitVerify(pubKey);

                // 更新整个文件内容
                onePass.Update(fileBytes, 0, fileBytes.Length);

                return onePass.Verify(signature);
            }
            else
            {
                return false; // 不是有效签名文件
            }

            // 5. 如果是 PgpSignatureList，验证流程
            if (signature != null)
            {
                PgpPublicKey pubKey = publicKeyRing.GetPublicKey(signature.KeyId);
                if (pubKey == null) return false;

                signature.InitVerify(pubKey);
                signature.Update(fileBytes, 0, fileBytes.Length);

                return signature.Verify();
            }

            return false;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// 在内存中生成密钥对
    /// </summary>
    /// <param name="identity">邮箱</param>
    /// <param name="passphrase">密码</param>
    /// <param name="keySize"></param>
    /// <returns></returns>
    public static (PgpPublicKeyRing pubRing, PgpSecretKeyRing secRing)
        GeneratePgpKeyPair(
            string identity,
            string passphrase,
            int keySize = 4096)
    {
        var random = new SecureRandom();

        // =====================================================
        // 1. 主密钥（Sign + Certify）
        // =====================================================
        var rsaGen = new RsaKeyPairGenerator();
        rsaGen.Init(new RsaKeyGenerationParameters(
            BigInteger.ValueOf(0x10001),
            random,
            keySize,
            12));

        var masterKeyPair = rsaGen.GenerateKeyPair();
        var masterPgpKeyPair = new PgpKeyPair(
            PublicKeyAlgorithmTag.RsaSign,
            masterKeyPair,
            DateTime.UtcNow);

        // =====================================================
        // 2. 主密钥 Subpacket
        // =====================================================
        var masterSubpacket = new PgpSignatureSubpacketGenerator();

        masterSubpacket.SetKeyFlags(
            true,
            PgpKeyFlags.CanSign | PgpKeyFlags.CanCertify);

        masterSubpacket.SetPreferredHashAlgorithms(
            true,
            ToIntArray(
                HashAlgorithmTag.Sha512,
                HashAlgorithmTag.Sha256)
        );

        masterSubpacket.SetPreferredSymmetricAlgorithms(
            true,
            ToIntArray(
                SymmetricKeyAlgorithmTag.Aes256,
                SymmetricKeyAlgorithmTag.Aes192,
                SymmetricKeyAlgorithmTag.Aes128)
        );

        masterSubpacket.SetFeature(
            true,
            Features.FEATURE_MODIFICATION_DETECTION);

        // =====================================================
        // 3. KeyRingGenerator（核心）
        // =====================================================
        var keyRingGen = new PgpKeyRingGenerator(
            PgpSignature.PositiveCertification,
            masterPgpKeyPair,
            identity,
            SymmetricKeyAlgorithmTag.Aes256,
            passphrase.ToCharArray(),
            true,
            masterSubpacket.Generate(),
            null,
            random);

        // =====================================================
        // 4. 加密子密钥
        // =====================================================
        var encKeyPair = rsaGen.GenerateKeyPair();
        var encPgpKeyPair = new PgpKeyPair(
            PublicKeyAlgorithmTag.RsaEncrypt,
            encKeyPair,
            DateTime.UtcNow);

        var encSubpacket = new PgpSignatureSubpacketGenerator();
        encSubpacket.SetKeyFlags(
            true,
            PgpKeyFlags.CanEncryptCommunications |
            PgpKeyFlags.CanEncryptStorage);

        // ✅ 2.7.0 的“添加子密钥”方式
        keyRingGen.AddSubKey(
            encPgpKeyPair,
            encSubpacket.Generate(),
            null);

        // =====================================================
        // 5. 生成 KeyRing
        // =====================================================
        PgpSecretKeyRing secretKeyRing = keyRingGen.GenerateSecretKeyRing();
        PgpPublicKeyRing publicKeyRing = keyRingGen.GeneratePublicKeyRing();

        return (publicKeyRing, secretKeyRing);
    }

    /// <summary>
    /// 导出内存中的公钥
    /// </summary>
    /// <param name="publicKeyRing"></param>
    /// <returns></returns>
    public static string ExportPublicKey(PgpPublicKeyRing publicKeyRing)
    {
        using var output = new MemoryStream();
        using var armored = new ArmoredOutputStream(output);
        publicKeyRing.Encode(armored);
        armored.Close();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// 导出内存中的私钥
    /// </summary>
    /// <param name="secretKeyRing"></param>
    /// <returns></returns>
    public static string ExportPrivateKey(PgpSecretKeyRing secretKeyRing)
    {
        using var output = new MemoryStream();
        using var armored = new ArmoredOutputStream(output);
        secretKeyRing.Encode(armored);
        armored.Close();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static int[] ToIntArray<TEnum>(params TEnum[] values)
        where TEnum : Enum
    {
        return values.Select(v => Convert.ToInt32(v)).ToArray();
    }

    /// <summary>
    /// 把文件解析为私钥
    /// </summary>
    /// <param name="secretKeyFilePath"></param>
    /// <returns></returns>
    public static PgpSecretKeyRing LoadSecretKeyRing(string secretKeyFilePath)
    {
        string fileType = GetPgpFileType(secretKeyFilePath);
        if (!"SecretKey".Equals(fileType))
        {
            return new PgpSecretKeyRing(Array.Empty<byte>());
        }

        using var keyStream = File.OpenRead(secretKeyFilePath);

        // PGP 文件可能是 ASCII-armored，也可能是二进制
        using var decoderStream = PgpUtilities.GetDecoderStream(keyStream);

        // 生成 KeyRing 对象
        PgpSecretKeyRing secretKeyRing = new PgpSecretKeyRing(decoderStream);

        return secretKeyRing;
    }

    /// <summary>
    /// 把文件解析为公钥
    /// </summary>
    /// <param name="secretKeyFilePath"></param>
    /// <returns></returns>
    public static PgpPublicKeyRing LoadPublicKeyRing(string secretKeyFilePath)
    {
        string fileType = GetPgpFileType(secretKeyFilePath);
        if (!"PublicKey".Equals(fileType))
        {
            return new PgpPublicKeyRing(Array.Empty<byte>());
        }

        using var keyStream = File.OpenRead(secretKeyFilePath);

        // PGP 文件可能是 ASCII-armored，也可能是二进制
        using var decoderStream = PgpUtilities.GetDecoderStream(keyStream);

        // 生成 KeyRing 对象
        PgpPublicKeyRing ring = new PgpPublicKeyRing(decoderStream);

        return ring;
    }

    public static PgpSignatureList LoadSignatureRing(string signatureFilePath)
    {
        string fileType = GetPgpFileType(signatureFilePath);
        if (!"Signature".Equals(fileType))
        {
            return new PgpSignatureList(Array.Empty<PgpSignature>());
        }

        using var sigStream = File.OpenRead(signatureFilePath);
        using var decoderStream = PgpUtilities.GetDecoderStream(sigStream);

        var pgpFactory = new PgpObjectFactory(decoderStream);
        var obj = pgpFactory.NextPgpObject();

        // 在 2.7+ 中，签名文件应直接返回 PgpSignatureList
        if (obj is PgpSignatureList signatureList)
        {
            return signatureList;
        }

        return new PgpSignatureList(Array.Empty<PgpSignature>());
    }

    public static string GetPgpFileType(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var decoder = PgpUtilities.GetDecoderStream(fs);

        PgpObjectFactory factory = new PgpObjectFactory(decoder);
        PgpObject obj = factory.NextPgpObject();

        return obj switch
        {
            PgpSecretKeyRing => "SecretKey",
            PgpPublicKeyRing => "PublicKey",
            PgpSignatureList => "Signature",
            _ => "Unknown"
        };
    }
}