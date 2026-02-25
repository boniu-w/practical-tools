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

/// <summary>
/// 提供基于 BouncyCastle 和外部 gpg 的常用 PGP 功能（加密、解密、签名、验签、密钥生成等）。
/// </summary>
public class GpgHelper
{
    private const string GpgPath = @"C:\Program Files (x86)\gnupg\bin\gpg.exe";

    /// <summary>
    /// 使用系统 gpg 对文件进行加密并输出到指定文件（通过接收者标识符）。
    /// </summary>
    /// <param name="inputFile">要加密的源文件路径。</param>
    /// <param name="outputFile">输出密文文件路径。</param>
    /// <param name="recipient">接收方的密钥标识（name 或 key id）。</param>
    /// <exception cref="ArgumentNullException">当任一参数为空或空白时抛出。</exception>
    public static void EncryptFile1(string inputFile, string outputFile, string recipient)
    {
        if (string.IsNullOrWhiteSpace(inputFile)) throw new ArgumentNullException(nameof(inputFile));
        if (string.IsNullOrWhiteSpace(outputFile)) throw new ArgumentNullException(nameof(outputFile));
        if (string.IsNullOrWhiteSpace(recipient)) throw new ArgumentNullException(nameof(recipient));

        var args = $"--batch --yes --output \"{outputFile}\" --encrypt --recipient \"{recipient}\" \"{inputFile}\"";
        RunGpgCommand(args);
    }

    /// <summary>
    /// 使用系统 gpg 解密指定输入文件到输出文件（假设私钥已在本地 gpg 环境中可用）。
    /// </summary>
    /// <param name="inputFile">密文文件路径。</param>
    /// <param name="outputFile">解密后输出文件路径。</param>
    /// <exception cref="ArgumentNullException">当参数为空或空白时抛出。</exception>
    public static void DecryptFile(string inputFile, string outputFile)
    {
        if (string.IsNullOrWhiteSpace(inputFile)) throw new ArgumentNullException(nameof(inputFile));
        if (string.IsNullOrWhiteSpace(outputFile)) throw new ArgumentNullException(nameof(outputFile));

        var args = $"--output \"{outputFile}\" --decrypt \"{inputFile}\"";
        RunGpgCommand(args);
    }

    /// <summary>
    /// 同步包装：运行 gpg 命令并阻塞直到完成。
    /// </summary>
    /// <param name="arguments">传递给 gpg 的命令参数。</param>
    /// <param name="timeoutMilliseconds">可选超时（毫秒）。目前内部未严格使用超时来取消进程。</param>
    private static void RunGpgCommand(string arguments, int timeoutMilliseconds = 60_000)
    {
        // 同步包装异步实现
        RunGpgCommandAsync(arguments, timeoutMilliseconds).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步运行 gpg 进程，读取标准输出和错误，并在非零退出码时抛出异常。
    /// </summary>
    /// <param name="arguments">传递给 gpg 的命令参数。</param>
    /// <param name="timeoutMilliseconds">可选超时（毫秒）。</param>
    /// <returns>完成任务。</returns>
    /// <exception cref="Exception">运行 gpg 失败或退出码非零时抛出。</exception>
    private static async Task RunGpgCommandAsync(string arguments, int timeoutMilliseconds = 60_000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GpgPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
#if NET6_0_OR_GREATER
            var waitTask = process.WaitForExitAsync();
#else
        var waitTask = Task.Run(() => process.WaitForExit());
#endif
            // 等待所有任务完成
            await Task.WhenAll(outputTask, errorTask, waitTask).ConfigureAwait(false);

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);

            if (!process.HasExited)
            {
                if (!process.CloseMainWindow())
                {
                    process.Kill(true);
                }
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"GPG error (exit {process.ExitCode}): {error}\n{output}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to run gpg: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从包含公钥的流中读取第一个可用于加密的公钥。
    /// </summary>
    /// <param name="publicKeyStream">包含公钥的输入流（可为 armored 或二进制）。</param>
    /// <returns>可用于加密的 <see cref="PgpPublicKey"/>。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="publicKeyStream"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当未找到可用于加密的公钥时抛出。</exception>
    public static PgpPublicKey ReadPublicKey(Stream publicKeyStream)
    {
        if (publicKeyStream == null) throw new ArgumentNullException(nameof(publicKeyStream));

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
    /// 从私钥流和 passphrase 中提取私钥对象（第一个匹配的）。
    /// </summary>
    /// <param name="privateKeyStream">包含私钥的流（armored 或二进制）。</param>
    /// <param name="passphrase">用于解锁私钥的口令，可能为 null。</param>
    /// <returns>提取到的 <see cref="PgpPrivateKey"/>。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="privateKeyStream"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当未找到可用私钥或 passphrase 错误时抛出。</exception>
    public static PgpPrivateKey ReadPrivateKey(Stream privateKeyStream, string passphrase)
    {
        if (privateKeyStream == null) throw new ArgumentNullException(nameof(privateKeyStream));

        using var input = PgpUtilities.GetDecoderStream(privateKeyStream);
        var bundle = new PgpSecretKeyRingBundle(input);

        foreach (PgpSecretKeyRing ring in bundle.GetKeyRings())
        {
            foreach (PgpSecretKey key in ring.GetSecretKeys())
            {
                try
                {
                    var privateKey = key.ExtractPrivateKey(passphrase?.ToCharArray());
                    if (privateKey != null)
                        return privateKey;
                }
                catch (PgpException)
                {
                    // 密码不匹配或校验失败，尝试下一个
                }
            }
        }

        throw new ArgumentException("未找到可用的私钥，或 passphrase 错误");
    }

    /// <summary>
    /// 使用公钥字符串对文件进行加密（内存加载公钥并调用内部实现）。
    /// </summary>
    /// <param name="inputFile">要加密的输入文件路径。</param>
    /// <param name="outputFile">输出密文文件路径。</param>
    /// <param name="publicKeyString">公钥的 ASCII-armored 字符串表示。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="publicKeyString"/> 为 null 时抛出。</exception>
    public static async Task EncryptFileByPublicString(string inputFile, string outputFile, string publicKeyString)
    {
        if (publicKeyString == null) throw new ArgumentNullException(nameof(publicKeyString));
        await Task.Run(() =>
        {
            using var pubKeyStream = new MemoryStream(Encoding.UTF8.GetBytes(publicKeyString));
            EncryptFileBy(inputFile, outputFile, pubKeyStream);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用提供的公钥流对文件进行 PGP 加密并写入输出文件（流方式实现）。
    /// </summary>
    /// <param name="inputFile">要加密的源文件路径。</param>
    /// <param name="outputFile">密文输出文件路径。</param>
    /// <param name="pubKeyStream">公钥流。</param>
    private static void EncryptFileBy(string inputFile, string outputFile, Stream pubKeyStream)
    {
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
    /// 使用公钥字符串对文件进行加密，支持设定是否输出 ASCII-armored。
    /// </summary>
    /// <param name="outputFile">密文输出路径。</param>
    /// <param name="inputFile">要加密的输入文件路径。</param>
    /// <param name="publicKeyString">公钥的 ASCII-armored 字符串表示。</param>
    /// <param name="armor">是否输出 ASCII-armored（默认 true）。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="publicKeyString"/> 为 null 时抛出。</exception>
    public static void EncryptFile(
        string outputFile,
        string inputFile,
        string publicKeyString,
        bool armor = true)
    {
        if (publicKeyString == null) throw new ArgumentNullException(nameof(publicKeyString));

        using var outputStream = File.Create(outputFile);
        Stream finalOut = outputStream;

        if (armor)
            finalOut = new ArmoredOutputStream(outputStream);

        var encGen = new PgpEncryptedDataGenerator(
            SymmetricKeyAlgorithmTag.Aes256,
            true,
            new SecureRandom());

        using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(publicKeyString));
        var encKey = ReadPublicKey(keyStream);

        encGen.AddMethod(encKey);

        using var encryptedOut = encGen.Open(finalOut, new byte[1 << 16]);
        var comData = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);

        using var compressedOut = comData.Open(encryptedOut);
        var literalData = new PgpLiteralDataGenerator();

        using var literalOut = literalData.Open(
            compressedOut,
            PgpLiteralData.Binary,
            Path.GetFileName(inputFile),
            new FileInfo(inputFile).Length,
            DateTime.Now);

        using var inputFileStream = File.OpenRead(inputFile);
        Streams.PipeAll(inputFileStream, literalOut);
    }

    /// <summary>
    /// 使用私钥文件和 passphrase 解密指定的 PGP 文件到输出文件（使用 BouncyCastle 流处理）。
    /// </summary>
    /// <param name="inputFile">密文文件路径。</param>
    /// <param name="outputFile">解密后的输出文件路径。</param>
    /// <param name="privateKeyFile">包含私钥的文件路径。</param>
    /// <param name="passphrase">私钥口令。</param>
    public static void DecryptFile(string inputFile, string outputFile, string privateKeyFile, string passphrase)
    {
        using var input = File.OpenRead(inputFile);
        using var decoder = PgpUtilities.GetDecoderStream(input);
        var pgpObjFactory = new PgpObjectFactory(decoder);
        var first = pgpObjFactory.NextPgpObject();
        var encDataList = first as PgpEncryptedDataList
                          ?? (PgpEncryptedDataList)pgpObjFactory.NextPgpObject();

        using var privKeyStream = File.OpenRead(privateKeyFile);

        DecryptFileBy(outputFile, privKeyStream, passphrase, encDataList);
    }

    /// <summary>
    /// 内部实现：使用私钥流解密给定的加密数据列表并写入输出文件。
    /// </summary>
    /// <param name="outputFile">解密输出路径。</param>
    /// <param name="privateKeyStream">私钥流（armored 或二进制）。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <param name="encDataList">来自 PGP 文件的加密数据列表。</param>
    /// <exception cref="Exception">当找不到匹配私钥时抛出。</exception>
    private static void DecryptFileBy(string outputFile, Stream privateKeyStream, string passphrase,
        PgpEncryptedDataList encDataList)
    {
        var privKey = ReadPrivateKey(privateKeyStream, passphrase);
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
    /// 使用内存中私钥字符串异步解密文件（包装同步实现）。
    /// </summary>
    /// <param name="inputFile">密文文件路径。</param>
    /// <param name="outputFile">解密输出路径。</param>
    /// <param name="privateKeyString">私钥的 ASCII-armored 字符串。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="privateKeyString"/> 为 null 时抛出。</exception>
    public static async Task DecryptFileByPublicString(string inputFile, string outputFile, string privateKeyString,
        string passphrase)
    {
        if (privateKeyString == null) throw new ArgumentNullException(nameof(privateKeyString));
        await Task.Run(() => { DecryptFile2(inputFile, outputFile, privateKeyString, passphrase); })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 从私钥字符串解密文件的实现（在内存中加载私钥并寻找匹配的 keyId）。
    /// </summary>
    /// <param name="inputFile">密文文件路径。</param>
    /// <param name="outputFile">解密输出路径。</param>
    /// <param name="privateKeyString">私钥的 ASCII-armored 字符串。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <exception cref="ArgumentException">当未找到匹配私钥或解密失败时抛出。</exception>
    public static void DecryptFile2(
        string inputFile,
        string outputFile,
        string privateKeyString,
        string passphrase)
    {
        using var input = File.OpenRead(inputFile);
        using var decoderStream = PgpUtilities.GetDecoderStream(input);

        using var memStream = new MemoryStream();
        decoderStream.CopyTo(memStream);
        memStream.Position = 0;

        var pgpObjFactory = new PgpObjectFactory(memStream);

        PgpEncryptedDataList encDataList = null;
        var obj = pgpObjFactory.NextPgpObject();

        if (obj is PgpEncryptedDataList list)
            encDataList = list;
        else
            encDataList = (PgpEncryptedDataList)pgpObjFactory.NextPgpObject();

        PgpPrivateKey privateKey = null;
        PgpPublicKeyEncryptedData encryptedData = null;

        foreach (PgpPublicKeyEncryptedData data in encDataList.GetEncryptedDataObjects())
        {
            using var privateKeyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKeyString));
            privateKey = FindPrivateKey(privateKeyStream, data.KeyId, passphrase);

            if (privateKey != null)
            {
                encryptedData = data;
                break;
            }
        }

        if (privateKey == null)
            throw new ArgumentException("未找到匹配的私钥");

        using var clear = encryptedData.GetDataStream(privateKey);
        var plainFact = new PgpObjectFactory(clear);
        var message = plainFact.NextPgpObject();

        if (message is PgpCompressedData compressedData)
        {
            var compFact = new PgpObjectFactory(compressedData.GetDataStream());
            message = compFact.NextPgpObject();
        }

        if (message is PgpLiteralData literalData)
        {
            using var unc = literalData.GetInputStream();
            using var output = File.Create(outputFile);
            Streams.PipeAll(unc, output);
        }
        else
            throw new PgpException("无法解析解密数据");

        if (encryptedData.IsIntegrityProtected() && !encryptedData.Verify())
            throw new PgpException("完整性校验失败");
    }

    /// <summary>
    /// 在给定的私钥流中查找指定 keyId 对应的私钥并提取为 <see cref="PgpPrivateKey"/>。
    /// </summary>
    /// <param name="privateKeyStream">包含私钥集合的流。</param>
    /// <param name="keyId">要寻找的 keyId。</param>
    /// <param name="passphrase">私钥口令，可能为 null。</param>
    /// <returns>找到并提取的私钥，找不到则返回 null。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="privateKeyStream"/> 为 null 时抛出。</exception>
    public static PgpPrivateKey FindPrivateKey(Stream privateKeyStream, long keyId, string passphrase)
    {
        if (privateKeyStream == null) throw new ArgumentNullException(nameof(privateKeyStream));

        using var decoderStream = PgpUtilities.GetDecoderStream(privateKeyStream);
        var secretKeyRingBundle = new PgpSecretKeyRingBundle(decoderStream);
        PgpSecretKey secretKey = secretKeyRingBundle.GetSecretKey(keyId);

        if (secretKey == null)
            return null;

        return secretKey.ExtractPrivateKey(passphrase?.ToCharArray());
    }

    /// <summary>
    /// 使用指定的密钥环和口令对字符串数据进行签名，返回 ASCII-armored 的签名文本。
    /// </summary>
    /// <param name="data">要签名的原始字符串。</param>
    /// <param name="secretKeyRing">用于签名的密钥环。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <returns>ASCII-armored 的签名字符串。</returns>
    /// <exception cref="ArgumentNullException">当必需参数为 null 时抛出。</exception>
    public static string SignData(string data, PgpSecretKeyRing secretKeyRing, string passphrase)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (secretKeyRing == null) throw new ArgumentNullException(nameof(secretKeyRing));

        PgpSecretKey secretKey = secretKeyRing.GetSecretKey();
        PgpPrivateKey privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());

        var signatureGenerator = new PgpSignatureGenerator(secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        signatureGenerator.InitSign(PgpSignature.BinaryDocument, privateKey);

        var subpacket = new PgpSignatureSubpacketGenerator();
        var userId = secretKey.PublicKey.GetUserIds().Cast<string>().FirstOrDefault() ?? string.Empty;
        subpacket.SetSignerUserId(false, userId);
        signatureGenerator.SetHashedSubpackets(subpacket.Generate());

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
    /// 验证给定字符串的 ASCII-armored 签名是否由提供的公钥环签名。
    /// </summary>
    /// <param name="data">原始数据字符串。</param>
    /// <param name="signatureAscii">ASCII-armored 签名文本。</param>
    /// <param name="publicKeyRing">用于验证的公钥环。</param>
    /// <returns>签名验证成功返回 true，否则 false（或抛出异常）。</returns>
    /// <exception cref="ArgumentNullException">当必需参数为 null 时抛出。</exception>
    public static bool VerifySignature(string data, string signatureAscii, PgpPublicKeyRing publicKeyRing)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (signatureAscii == null) throw new ArgumentNullException(nameof(signatureAscii));
        if (publicKeyRing == null) throw new ArgumentNullException(nameof(publicKeyRing));

        using var sigStream = new MemoryStream(Encoding.UTF8.GetBytes(signatureAscii));
        using var decoderStream = PgpUtilities.GetDecoderStream(sigStream);
        var pgpObjFactory = new PgpObjectFactory(decoderStream);

        var obj = pgpObjFactory.NextPgpObject();
        if (obj is not PgpSignatureList sigList || sigList.Count == 0)
            throw new Exception("Unexpected PGP object type");

        var signature = sigList[0];
        PgpPublicKey pubKey = publicKeyRing.GetPublicKey(signature.KeyId);
        signature.InitVerify(pubKey);

        var bytes = Encoding.UTF8.GetBytes(data);
        signature.Update(bytes, 0, bytes.Length);

        return signature.Verify();
    }

    /// <summary>
    /// 使用密钥环对指定文件进行二进制签名，返回签名的字节数组（非 armored）。
    /// </summary>
    /// <param name="exePath">要签名的文件路径。</param>
    /// <param name="secretKeyRing">签名私钥环。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <returns>签名字节数组。</returns>
    public static byte[] SignFile(
        string exePath,
        PgpSecretKeyRing secretKeyRing,
        string passphrase)
    {
        byte[] fileBytes = File.ReadAllBytes(exePath);
        PgpSecretKey secretKey = secretKeyRing.GetSecretKey();
        PgpPrivateKey privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());

        var sigGen = new PgpSignatureGenerator(
            secretKey.PublicKey.Algorithm,
            HashAlgorithmTag.Sha256);

        sigGen.InitSign(PgpSignature.BinaryDocument, privateKey);
        sigGen.Update(fileBytes, 0, fileBytes.Length);

        using var output = new MemoryStream();
        sigGen.Generate().Encode(output);

        return output.ToArray();
    }

    /// <summary>
    /// 辅助方法：对文件进行签名并把签名字节写入同名 .sig 文件，返回签名文件路径。
    /// </summary>
    /// <param name="exePath">要签名的文件路径。</param>
    /// <param name="secretKeyRing">签名私钥环。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <returns>生成的签名文件路径。</returns>
    public static string Test12(string exePath,
        PgpSecretKeyRing secretKeyRing,
        string passphrase)
    {
        byte[] sig = SignFile(exePath, secretKeyRing, passphrase);
        string sigFilePath = exePath + ".sig";
        File.WriteAllBytes(sigFilePath, sig);
        return sigFilePath;
    }

    /// <summary>
    /// 使用私钥环生成 ASCII-armored 的签名文件（写入 exePath + ".sig"），返回签名文件路径。
    /// </summary>
    /// <param name="exePath">要签名的文件路径。</param>
    /// <param name="secretKeyRing">签名私钥环。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <returns>生成的签名文件路径。</returns>
    public static string SignatureFile(string exePath,
        PgpSecretKeyRing secretKeyRing,
        string passphrase)
    {
        byte[] fileBytes = File.ReadAllBytes(exePath);
        PgpSecretKey secretKey = secretKeyRing.GetSecretKey();
        PgpPrivateKey privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());

        var sigGen = new PgpSignatureGenerator(secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        sigGen.InitSign(PgpSignature.BinaryDocument, privateKey);
        sigGen.Update(fileBytes, 0, fileBytes.Length);

        string sigFilePath = exePath + ".sig";
        using (var sigOut = File.Create(sigFilePath))
        using (var armored = new ArmoredOutputStream(sigOut))
        {
            sigGen.Generate().Encode(armored);
        }

        return sigFilePath;
    }

    /// <summary>
    /// 验证给定文件与其签名文件（非 armored / raw signature）是否匹配。
    /// </summary>
    /// <param name="exePath">被签名的文件路径。</param>
    /// <param name="sigPath">签名文件路径（原始签名字节或 ASCII-armored 皆可，方法会尝试解码）。</param>
    /// <param name="publicKeyRing">用于验证的公钥环。</param>
    /// <returns>验证成功返回 true，否则 false。</returns>
    public static bool VerifyFileSignature(
        string exePath,
        string sigPath,
        PgpPublicKeyRing publicKeyRing)
    {
        byte[] fileBytes = File.ReadAllBytes(exePath);
        byte[] sigBytes = File.ReadAllBytes(sigPath);

        using var sigStream = new MemoryStream(sigBytes);
        using var decoder = PgpUtilities.GetDecoderStream(sigStream);
        var factory = new PgpObjectFactory(decoder);

        var obj = factory.NextPgpObject();
        if (obj is not PgpSignatureList sigList || sigList.Count == 0)
            throw new Exception("Invalid signature format");

        var signature = sigList[0];
        PgpPublicKey pubKey = publicKeyRing.GetPublicKey(signature.KeyId);
        signature.InitVerify(pubKey);
        signature.Update(fileBytes, 0, fileBytes.Length);

        return signature.Verify();
    }

    /// <summary>
    /// 更稳健的文件签名验证：支持直接的签名列表或 one-pass 签名流式验证，遇到异常返回 false。
    /// </summary>
    /// <param name="exePath">被签名的文件路径。</param>
    /// <param name="sigPath">签名文件路径（可能包含 OnePass/Compressed/Literal/Signature 等对象）。</param>
    /// <param name="publicKeyRing">用于验证的公钥环。</param>
    /// <returns>验证成功返回 true，否则 false（包括解析异常等）。</returns>
    public static bool VerifyFileSignatureSafe(
        string exePath,
        string sigPath,
        PgpPublicKeyRing publicKeyRing)
    {
        try
        {
            byte[] fileBytes = File.ReadAllBytes(exePath);

            using var sigStream = File.OpenRead(sigPath);
            using var decoderStream = PgpUtilities.GetDecoderStream(sigStream);

            var pgpFactory = new PgpObjectFactory(decoderStream);
            PgpObject obj = pgpFactory.NextPgpObject();

            if (obj is PgpSignatureList sigList && sigList.Count > 0)
            {
                var signature = sigList[0];
                var pubKey = publicKeyRing.GetPublicKey(signature.KeyId);
                if (pubKey == null) return false;
                signature.InitVerify(pubKey);
                signature.Update(fileBytes, 0, fileBytes.Length);
                return signature.Verify();
            }
            else if (obj is PgpOnePassSignatureList onePassList && onePassList.Count > 0)
            {
                var onePass = onePassList[0];
                var pubKey = publicKeyRing.GetPublicKey(onePass.KeyId);
                if (pubKey == null) return false;

                onePass.InitVerify(pubKey);

                // 接着读取后续对象（通常是 PgpLiteralData 或 PgpCompressedData）
                var next = pgpFactory.NextPgpObject();

                Stream dataStream = next switch
                {
                    PgpCompressedData comp => comp.GetDataStream(),
                    PgpLiteralData lit => lit.GetInputStream(),
                    _ => null
                };

                if (dataStream == null) return false;

                // 读取并更新 onePass 签名
                using var ms = new MemoryStream();
                dataStream.CopyTo(ms);
                var bytes = ms.ToArray();
                onePass.Update(bytes, 0, bytes.Length);

                // 后续应该有 PgpSignatureList 包含最终签名
                var sigObj = pgpFactory.NextPgpObject();
                if (sigObj is not PgpSignatureList finalSigList || finalSigList.Count == 0) return false;
                var finalSig = finalSigList[0];

                return onePass.Verify(finalSig);
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 生成 PGP 密钥对并返回环对象；此方法当前仅生成并返回，不写入磁盘。
    /// </summary>
    /// <param name="identity">密钥的标识（通常是 name &lt;email&gt;）。</param>
    /// <param name="passphrase">用于保护私钥的口令。</param>
    /// <param name="keySize">RSA 密钥大小（位）。</param>
    /// <param name="keyLifeSeconds">可选的密钥过期时间（秒）。</param>
    public static void GeneratePgpKeyPairFile(string identity,
        string passphrase,
        int keySize = 4096,
        long? keyLifeSeconds = null)
    {
        var (pubRing, secRing, beginsOn, endOn) = GeneratePgpKeyPair(identity, passphrase, keySize, keyLifeSeconds);
        // 如需写入文件，可在此添加
    }

    /// <summary>
    /// 生成 PGP 公/私钥环并返回，同时返回生效时间和可选到期时间。
    /// </summary>
    /// <param name="identity">密钥标识。</param>
    /// <param name="passphrase">私钥保护口令。</param>
    /// <param name="keySize">RSA 密钥大小（位）。</param>
    /// <param name="keyLifeSeconds">可选过期时间（秒）。</param>
    /// <returns>包含公钥环、私钥环、开始时间和结束时间的元组。</returns>
    public static (PgpPublicKeyRing pubRing, PgpSecretKeyRing secRing, DateTime beginsOn, DateTime? endsOn)
        GeneratePgpKeyPair(
            string identity,
            string passphrase,
            int keySize = 2048,
            long? keyLifeSeconds = null)
    {
        DateTime now = DateTime.Now;
        DateTime beginsOn = now;
        DateTime? endsOn = null;
        if (keyLifeSeconds.HasValue) endsOn = now.AddSeconds(keyLifeSeconds.Value);

        var random = new SecureRandom();

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
            now);

        var masterSubpacket = new PgpSignatureSubpacketGenerator();
        masterSubpacket.SetKeyFlags(true, PgpKeyFlags.CanSign | PgpKeyFlags.CanCertify);
        masterSubpacket.SetPreferredHashAlgorithms(true, ToIntArray(HashAlgorithmTag.Sha512, HashAlgorithmTag.Sha256));
        masterSubpacket.SetPreferredSymmetricAlgorithms(true, ToIntArray(SymmetricKeyAlgorithmTag.Aes256, SymmetricKeyAlgorithmTag.Aes192, SymmetricKeyAlgorithmTag.Aes128));
        masterSubpacket.SetFeature(true, Features.FEATURE_MODIFICATION_DETECTION);
        if (keyLifeSeconds.HasValue) masterSubpacket.SetKeyExpirationTime(true, keyLifeSeconds.Value);

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

        var encKeyPair = rsaGen.GenerateKeyPair();
        var encPgpKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaEncrypt, encKeyPair, now);

        var encSubpacket = new PgpSignatureSubpacketGenerator();
        encSubpacket.SetKeyFlags(true, PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage);
        if (keyLifeSeconds.HasValue) encSubpacket.SetKeyExpirationTime(true, keyLifeSeconds.Value);

        keyRingGen.AddSubKey(encPgpKeyPair, encSubpacket.Generate(), null);

        PgpSecretKeyRing secretKeyRing = keyRingGen.GenerateSecretKeyRing();
        PgpPublicKeyRing publicKeyRing = keyRingGen.GeneratePublicKeyRing();

        return (publicKeyRing, secretKeyRing, beginsOn, endsOn);
    }

    /// <summary>
    /// 导出公钥环为 ASCII-armored 字符串。
    /// </summary>
    /// <param name="publicKeyRing">要导出的公钥环。</param>
    /// <returns>ASCII-armored 的公钥字符串。</returns>
    public static string ExportPublicKey(PgpPublicKeyRing publicKeyRing)
    {
        using var output = new MemoryStream();
        using var armored = new ArmoredOutputStream(output);
        publicKeyRing.Encode(armored);
        armored.Close();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// 导出私钥环为 ASCII-armored 字符串。
    /// </summary>
    /// <param name="secretKeyRing">要导出的私钥环。</param>
    /// <returns>ASCII-armored 的私钥字符串。</returns>
    public static string ExportPrivateKey(PgpSecretKeyRing secretKeyRing)
    {
        using var output = new MemoryStream();
        using var armored = new ArmoredOutputStream(output);
        secretKeyRing.Encode(armored);
        armored.Close();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// 将枚举值数组转换为对应的 int 数组（用于 BouncyCastle 的偏好设置）。
    /// </summary>
    /// <typeparam name="TEnum">枚举类型。</typeparam>
    /// <param name="values">枚举值列表。</param>
    /// <returns>对应的 int 数组。</returns>
    private static int[] ToIntArray<TEnum>(params TEnum[] values)
        where TEnum : Enum
    {
        return values.Select(v => Convert.ToInt32(v)).ToArray();
    }

    /// <summary>
    /// 从文件路径加载私钥环；如果文件不是私钥环类型则返回空的 <see cref="PgpSecretKeyRing"/>。
    /// </summary>
    /// <param name="secretKeyFilePath">私钥文件路径。</param>
    /// <returns>加载得到的 <see cref="PgpSecretKeyRing"/>。</returns>
    public static PgpSecretKeyRing LoadSecretKeyRing(string secretKeyFilePath)
    {
        string fileType = GetPgpFileType(secretKeyFilePath);
        if (!"SecretKey".Equals(fileType))
        {
            return new PgpSecretKeyRing(Array.Empty<byte>());
        }

        using var keyStream = File.OpenRead(secretKeyFilePath);
        using var decoderStream = PgpUtilities.GetDecoderStream(keyStream);
        PgpSecretKeyRing secretKeyRing = new PgpSecretKeyRing(decoderStream);
        return secretKeyRing;
    }

    /// <summary>
    /// 从文件路径加载公钥环；如果文件不是公钥环类型则返回空的 <see cref="PgpPublicKeyRing"/>。
    /// </summary>
    /// <param name="secretKeyFilePath">公钥文件路径。</param>
    /// <returns>加载得到的 <see cref="PgpPublicKeyRing"/>。</returns>
    public static PgpPublicKeyRing LoadPublicKeyRing(string secretKeyFilePath)
    {
        string fileType = GetPgpFileType(secretKeyFilePath);
        if (!"PublicKey".Equals(fileType))
        {
            return new PgpPublicKeyRing(Array.Empty<byte>());
        }

        using var keyStream = File.OpenRead(secretKeyFilePath);
        using var decoderStream = PgpUtilities.GetDecoderStream(keyStream);
        PgpPublicKeyRing ring = new PgpPublicKeyRing(decoderStream);
        return ring;
    }

    /// <summary>
    /// 从签名文件加载签名列表；若解析失败返回空的 <see cref="PgpSignatureList"/>。
    /// </summary>
    /// <param name="signatureFilePath">签名文件路径。</param>
    /// <returns>加载得到的 <see cref="PgpSignatureList"/>。</returns>
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

        if (obj is PgpSignatureList signatureList)
        {
            return signatureList;
        }

        return new PgpSignatureList(Array.Empty<PgpSignature>());
    }

    /// <summary>
    /// 尝试判断给定 PGP 文件的顶层对象类型，返回 "SecretKey"、"PublicKey"、"Signature" 或 "Unknown"。
    /// </summary>
    /// <param name="filePath">PGP 文件路径。</param>
    /// <returns>描述文件类型的字符串。</returns>
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
