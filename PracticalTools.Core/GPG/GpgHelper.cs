using System.Diagnostics;
using System.IO;
using System.Text;
using PracticalTools.Storage;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg.Sig;
using Org.BouncyCastle.Crypto;
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
    /// <summary>
    /// gpg.exe 的完整路径。外部设置（如测试项目），未设置则为 null。
    /// 仅 SetKeyExpiration、GetKeyFingerprints 等方法需要。
    /// </summary>
    public static string? GpgExePath { get; set; }

    /// <summary>
    /// 确保 gpg.exe 可用，否则抛出明确异常。
    /// </summary>
    private static void RequireGpg()
    {
        if (GpgExePath == null)
            throw new InvalidOperationException(
                "此功能需要外部 gpg.exe，但当前环境中未找到。请安装 GnuPG (Gpg4win 或 gnupg-w32) 后重试。");
    }

    /// <summary>
    /// 使用系统 gpg 对文件进行加密并输出到指定文件（通过接收者标识符）。
    /// </summary>
    /// <param name="inputFile">要加密的源文件路径。</param>
    /// <param name="outputFile">输出密文文件路径。</param>
    /// <param name="recipient">接收方的密钥标识（name 或 key id）。</param>
    /// <exception cref="ArgumentNullException">当任一参数为空或空白时抛出。</exception>
    public static void EncryptFile1(string inputFile, string outputFile, string recipient)
    {
        RequireGpg();
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
        RequireGpg();
        if (string.IsNullOrWhiteSpace(inputFile)) throw new ArgumentNullException(nameof(inputFile));
        if (string.IsNullOrWhiteSpace(outputFile)) throw new ArgumentNullException(nameof(outputFile));

        var args = $"--output \"{outputFile}\" --decrypt \"{inputFile}\"";
        RunGpgCommand(args);
    }

    /// <summary>
    /// 修改指定密钥的过期时间（使用 gpg --quick-set-expire）。
    /// 需要提供私钥口令以重新签名密钥。
    /// </summary>
    /// <param name="fingerprint">密钥指纹（完整或部分，只要能唯一标识）。</param>
    /// <param name="passphrase">私钥口令（用于重新签名）。</param>
    /// <param name="expireString">
    /// 过期时间字符串，支持以下格式：
    /// "0" — 永不过期；
    /// "2027-12-31" — 指定日期；
    /// "2y" — 2 年后；
    /// "6m" — 6 个月后；
    /// "30d" — 30 天后；
    /// "1w" — 1 周后。
    /// </param>
    /// <param name="subkeyFingerprint">可选：仅修改指定子密钥的过期时间，null 则修改整个主密钥及其所有子密钥。</param>
    /// <exception cref="ArgumentNullException">当任一参数为空或空白时抛出。</exception>
    public static void SetKeyExpiration(string fingerprint, string passphrase, string expireString, string? subkeyFingerprint = null)
    {
        RequireGpg();
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new ArgumentNullException(nameof(fingerprint));
        if (string.IsNullOrWhiteSpace(passphrase)) throw new ArgumentNullException(nameof(passphrase));
        if (string.IsNullOrWhiteSpace(expireString)) throw new ArgumentNullException(nameof(expireString));

        var subkeyPart = subkeyFingerprint != null ? $" \"{subkeyFingerprint}\"" : "";
        var args = $"--batch --yes --pinentry-mode loopback --passphrase \"{passphrase}\" --quick-set-expire \"{fingerprint}\" \"{expireString}\"{subkeyPart}";

        RunGpgCommand(args);
    }

    /// <summary>
    /// 修改密钥对的过期时间（基于 BouncyCastle，无需 gpg.exe）。
    /// 使用原有密钥材料重建密钥环，仅更新有效期签名。
    /// </summary>
    /// <param name="secRing">私钥环。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <param name="keyLifeSeconds">新的过期时间（秒），0 表示永不过期。</param>
    /// <returns>修改后的公钥环和私钥环。</returns>
    public static (PgpPublicKeyRing pubRing, PgpSecretKeyRing secRing) SetKeyExpiration(
        PgpSecretKeyRing secRing,
        string passphrase,
        long keyLifeSeconds)
    {
        // 1. 获取主密钥的 RSA 参数
        var masterSecKey = secRing.GetSecretKey()
            ?? throw new ArgumentException("密钥环中没有找到主密钥");
        var masterPubKey = masterSecKey.PublicKey;
        var masterPrivate = masterSecKey.ExtractPrivateKey(passphrase.ToCharArray());

        var rsaPriv = masterPrivate.Key as RsaPrivateCrtKeyParameters
            ?? throw new NotSupportedException("仅支持 RSA 密钥");

        var rsaPub = new RsaKeyParameters(false, rsaPriv.Modulus, rsaPriv.PublicExponent);
        var masterKeyPair = new AsymmetricCipherKeyPair(rsaPub, rsaPriv);
        var masterPgpKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaSign, masterKeyPair, masterPubKey.CreationTime);

        // 2. 获取用户标识
        string identity = string.Empty;
        foreach (var uid in masterPubKey.GetUserIds())
        {
            if (uid != null)
            {
                identity = uid.ToString()!;
                break;
            }
        }

        // 3. 构建主密钥
        var masterSubpacket = new PgpSignatureSubpacketGenerator();
        masterSubpacket.SetKeyFlags(true, PgpKeyFlags.CanSign | PgpKeyFlags.CanCertify);
        masterSubpacket.SetPreferredHashAlgorithms(true,
            ToIntArray(HashAlgorithmTag.Sha512, HashAlgorithmTag.Sha256));
        masterSubpacket.SetPreferredSymmetricAlgorithms(true,
            ToIntArray(SymmetricKeyAlgorithmTag.Aes256, SymmetricKeyAlgorithmTag.Aes192,
                SymmetricKeyAlgorithmTag.Aes128));
        masterSubpacket.SetFeature(true, Features.FEATURE_MODIFICATION_DETECTION);
        if (keyLifeSeconds > 0)
            masterSubpacket.SetKeyExpirationTime(true, keyLifeSeconds);

        var keyRingGen = new PgpKeyRingGenerator(
            PgpSignature.PositiveCertification,
            masterPgpKeyPair,
            identity,
            SymmetricKeyAlgorithmTag.Aes256,
            passphrase.ToCharArray(),
            true,
            masterSubpacket.Generate(),
            null,
            new SecureRandom());

        // 4. 重建加密子密钥（使用相同密钥材料）
        foreach (PgpSecretKey subSecKey in secRing.GetSecretKeys())
        {
            if (subSecKey.IsMasterKey)
                continue;

            if (!subSecKey.IsSigningKey)
            {
                var subPrivate = subSecKey.ExtractPrivateKey(passphrase.ToCharArray());
                var subRsaPriv = subPrivate.Key as RsaPrivateCrtKeyParameters;
                if (subRsaPriv == null) continue;

                var subRsaPub = new RsaKeyParameters(false, subRsaPriv.Modulus, subRsaPriv.PublicExponent);
                var subKeyPair = new AsymmetricCipherKeyPair(subRsaPub, subRsaPriv);
                var subPgpKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaEncrypt, subKeyPair, subSecKey.PublicKey.CreationTime);

                var encSubpacket = new PgpSignatureSubpacketGenerator();
                encSubpacket.SetKeyFlags(true, PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage);
                if (keyLifeSeconds > 0)
                    encSubpacket.SetKeyExpirationTime(true, keyLifeSeconds);

                keyRingGen.AddSubKey(subPgpKeyPair, encSubpacket.Generate(), null);
            }
        }

        return (keyRingGen.GeneratePublicKeyRing(), keyRingGen.GenerateSecretKeyRing());
    }

    /// <summary>
    /// 修改密钥过期时间（字符串版本，方便传入/传出 armored 密钥文本）。
    /// </summary>
    /// <param name="armoredSecretKey">ASCII-armored 私钥文本。</param>
    /// <param name="passphrase">私钥口令。</param>
    /// <param name="keyLifeSeconds">新的过期时间（秒），0 表示永不过期。</param>
    /// <returns>修改后的 armored 公钥和私钥文本。</returns>
    public static (string armoredPub, string armoredSec) SetKeyExpiration(
        string armoredSecretKey,
        string passphrase,
        long keyLifeSeconds)
    {
        using var secStream = new MemoryStream(Encoding.UTF8.GetBytes(armoredSecretKey));
        var secRing = new PgpSecretKeyRing(PgpUtilities.GetDecoderStream(secStream));

        var (pubRing, newSecRing) = SetKeyExpiration(secRing, passphrase, keyLifeSeconds);

        return (ExportPublicKey(pubRing), ExportPrivateKey(newSecRing));
    }

    /// <summary>
    /// 从公钥环计算密钥指纹（基于 BouncyCastle，无需 gpg.exe）。
    /// </summary>
    /// <param name="pubRing">公钥环。</param>
    /// <returns>40 位十六进制指纹字符串（大写）。</returns>
    public static string GetFingerprint(PgpPublicKeyRing pubRing)
    {
        var masterKey = pubRing.GetPublicKey();
        return BitConverter.ToString(masterKey.GetFingerprint()).Replace("-", "");
    }

    /// <summary>
    /// 从 armored 公钥字符串计算密钥指纹。
    /// </summary>
    /// <param name="armoredPublicKey">ASCII-armored 公钥文本。</param>
    /// <returns>40 位十六进制指纹字符串（大写）。</returns>
    public static string GetFingerprint(string armoredPublicKey)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(armoredPublicKey));
        var pubRing = new PgpPublicKeyRing(PgpUtilities.GetDecoderStream(stream));
        return GetFingerprint(pubRing);
    }

    /// <summary>
    /// 从公钥环获取 64 位 Key ID（指纹低 8 字节）。
    /// </summary>
    public static long GetKeyId(PgpPublicKeyRing pubRing)
    {
        return pubRing.GetPublicKey().KeyId;
    }

    /// <summary>
    /// </summary>
    /// <param name="identityPattern">要查找的标识（name 或 email），为 null 或空则返回所有密钥的指纹。</param>
    /// <returns>找到的指纹数组，未找到返回空数组。</returns>
    public static string[] GetKeyFingerprints(string? identityPattern = null)
    {
        RequireGpg();
        var pattern = !string.IsNullOrWhiteSpace(identityPattern) ? $" \"{identityPattern}\"" : "";
        string output = RunGpgAndGetOutput($"--list-keys --with-colons{pattern}");

        var fingerprints = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("fpr:"))
            {
                var parts = trimmed.Split(':');
                if (parts.Length > 9 && !string.IsNullOrEmpty(parts[9]))
                    fingerprints.Add(parts[9]);
            }
        }
        return fingerprints.ToArray();
    }

    /// <summary>
    /// 运行 gpg 命令并返回标准输出内容（不抛异常，仅返回空字符串）。
    /// </summary>
    private static string RunGpgAndGetOutput(string arguments, int timeoutMilliseconds = 30_000)
    {
        try
        {
            var tcs = new TaskCompletionSource<string>();
            var task = Task.Run(async () =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = GpgExePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
#if NET6_0_OR_GREATER
                var waitTask = process.WaitForExitAsync();
#else
                var waitTask = Task.Run(() => process.WaitForExit());
#endif
                var completed = await Task.WhenAny(waitTask, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);

                if (completed != waitTask)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    return string.Empty;
                }

                return await outputTask.ConfigureAwait(false);
            });

            return task.GetAwaiter().GetResult();
        }
        catch
        {
            return string.Empty;
        }
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
        var startInfo = new ProcessStartInfo
        {
            FileName = GpgExePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

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

            // Wait for either the process to exit or the timeout
            var completed = await Task.WhenAny(waitTask, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);

            if (completed != waitTask)
            {
                // timeout occurred
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // ignore kill exceptions
                }

                throw new TimeoutException($"GPG command timed out after {timeoutMilliseconds} ms");
            }

            // process exited; ensure output and error tasks complete
            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);

            if (!process.HasExited)
            {
                try
                {
                    if (!process.CloseMainWindow())
                        process.Kill(true);
                }
                catch
                {
                    // ignore
                }
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"GPG error (exit {process.ExitCode}): {error}\n{output}");
            }
        }
        catch (Exception ex) when (ex is not TimeoutException)
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
    internal static void EncryptFileForArchive(string inputFile, string outputFile, string publicKey,
        CancellationToken cancellationToken, IProgress<long>? progress)
    {
        using var key = new MemoryStream(Encoding.UTF8.GetBytes(publicKey));
        EncryptFileBy(inputFile, outputFile, key, cancellationToken, progress);
    }

    private static void EncryptFileBy(string inputFile, string outputFile, Stream pubKeyStream,
        CancellationToken cancellationToken = default, IProgress<long>? progress = null)
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
        var buffer = new byte[65536];
        long copied = 0;
        var timer = Stopwatch.StartNew();
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            literalOut.Write(buffer, 0, read);
            copied += read;
            if (timer.ElapsedMilliseconds >= 100) { progress?.Report(copied); timer.Restart(); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(copied);
        cancellationToken.ThrowIfCancellationRequested();
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
         PgpPublicKeyEncryptedData? encData = null;

         foreach (PgpEncryptedData ed in encDataList.GetEncryptedDataObjects())
         {
             if (ed is PgpPublicKeyEncryptedData data && data.KeyId == privKey.KeyId)
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
        DecryptFileStreaming(inputFile, outputFile, privateKeyString, passphrase, CancellationToken.None);
    }

    /// <summary>
    /// 流式解密预览文件，校验完整性后返回 PGP 中的原文件名。取消时由预览会话清理临时输出。
    /// </summary>
    public static Task<string> DecryptFileForPreviewAsync(string inputFile, string outputFile,
        string privateKeyString, string passphrase, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => DecryptFileStreaming(inputFile, outputFile, privateKeyString,
            passphrase, cancellationToken), cancellationToken);
    }

    internal static string DecryptFileForArchive(string inputFile, string outputFile, string secretKey, string passphrase,
        CancellationToken token, IProgress<long>? progress, long maxOutputBytes) =>
        DecryptFileStreaming(inputFile, outputFile, secretKey, passphrase, token, progress, maxOutputBytes, requireIntegrity: true);

    private static string DecryptFileStreaming(string inputFile, string outputFile,
        string privateKeyString, string passphrase, CancellationToken cancellationToken,
        IProgress<long>? progress = null, long maxOutputBytes = long.MaxValue, bool requireIntegrity = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = requireIntegrity ? LocalFileAccess.Open(inputFile) : File.OpenRead(inputFile);
        if (requireIntegrity && !input.CanSeek) throw new InvalidDataException("文件夹归档输入必须是可定位的普通文件。");
        using var decoderStream = PgpUtilities.GetDecoderStream(input);
        var pgpObjFactory = new PgpObjectFactory(decoderStream);

        var obj = pgpObjFactory.NextPgpObject();
        var encDataList = obj as PgpEncryptedDataList
            ?? pgpObjFactory.NextPgpObject() as PgpEncryptedDataList
            ?? throw new PgpException("文件不是有效的 PGP 加密文件。");

        PgpPrivateKey? privateKey = null;
        PgpPublicKeyEncryptedData? encryptedData = null;

        foreach (PgpPublicKeyEncryptedData data in encDataList.GetEncryptedDataObjects())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var privateKeyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKeyString));
            privateKey = FindPrivateKey(privateKeyStream, data.KeyId, passphrase);

            if (privateKey != null)
            {
                encryptedData = data;
                break;
            }
        }

        if (privateKey == null || encryptedData == null)
            throw new ArgumentException("未找到匹配的私钥");

        if (requireIntegrity && !encryptedData.IsIntegrityProtected())
            throw new PgpException("文件夹归档必须包含 PGP 完整性保护。");

        using var clear = encryptedData.GetDataStream(privateKey);
        var plainFact = new PgpObjectFactory(clear);
        var message = plainFact.NextPgpObject();

        var compressionDepth = 0;
        while (message is PgpCompressedData compressedData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requireIntegrity && ++compressionDepth > 8) throw new PgpException("PGP 压缩嵌套层数超过限制。");
            var compFact = new PgpObjectFactory(compressedData.GetDataStream());
            message = compFact.NextPgpObject();
        }

        string originalFileName;
        if (message is PgpLiteralData literalData)
        {
            originalFileName = literalData.FileName;
            using var unc = literalData.GetInputStream();
            using var output = File.Create(outputFile);
            var buffer = new byte[64 * 1024];
            long copied = 0;
            var timer = Stopwatch.StartNew();
            int read;
            while ((read = unc.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (read > maxOutputBytes - copied) throw new InvalidDataException("解密出的 ZIP 超过大小限制。");
                output.Write(buffer, 0, read);
                copied += read;
                if (timer.ElapsedMilliseconds >= 100) { progress?.Report(copied); timer.Restart(); }
            }
            progress?.Report(copied);
        }
        else
            throw new PgpException("无法解析解密数据");

        if (encryptedData.IsIntegrityProtected() && !encryptedData.Verify())
            throw new PgpException("完整性校验失败");

        cancellationToken.ThrowIfCancellationRequested();
        return originalFileName;
    }

    /// <summary>
    /// 在给定的私钥流中查找指定 keyId 对应的私钥并提取为 <see cref="PgpPrivateKey"/>。
    /// </summary>
    /// <param name="privateKeyStream">包含私钥集合的流。</param>
    /// <param name="keyId">要寻找的 keyId。</param>
    /// <param name="passphrase">私钥口令，可能为 null。</param>
    /// <returns>找到并提取的私钥，找不到则返回 null。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="privateKeyStream"/> 为 null 时抛出。</exception>
    public static PgpPrivateKey? FindPrivateKey(Stream privateKeyStream, long keyId, string passphrase)
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
        // Extract the first user id safely without redundant LINQ casts
        string userId = string.Empty;
        foreach (var uid in secretKey.PublicKey.GetUserIds())
        {
            if (uid != null)
            {
                userId = uid.ToString() ?? string.Empty;
                break;
            }
        }
        // Use the modern AddSignerUserId overload (isCritical, userId)
        subpacket.AddSignerUserId(false, userId);
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

                Stream? dataStream = next switch
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
         _ = GeneratePgpKeyPair(identity, passphrase, keySize, keyLifeSeconds);
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
        masterSubpacket.SetPreferredSymmetricAlgorithms(true,
            ToIntArray(SymmetricKeyAlgorithmTag.Aes256, SymmetricKeyAlgorithmTag.Aes192,
                SymmetricKeyAlgorithmTag.Aes128));
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
    /// 生成密钥对, 并指定密钥的生效时间和可选的过期时间；返回公钥环、私钥环以及生效/过期时间。
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="passphrase"></param>
    /// <param name="beginsOn">密钥开始时间</param>
    /// <param name="endsOn">密钥结束时间</param>
    /// <param name="keySize"></param>
    /// <param name="keyLifeSeconds"></param>
    /// <returns></returns>
    public static (PgpPublicKeyRing pubRing, PgpSecretKeyRing secRing, DateTime beginsOn, DateTime? endsOn)
        GeneratePgpKeyPair2(
            string identity,
            string passphrase,
            DateTime beginsOn,
            DateTime? endsOn = null,
            int keySize = 2048,
            long? keyLifeSeconds = null)
    {
        // Validate and derive the effective key lifetime (in seconds).
        // Behavior and precedence:
        // - If caller provides an explicit endsOn, use it and compute keyLifeSeconds = endsOn - beginsOn.
        // - Else if caller provides keyLifeSeconds, use that and compute endsOn = beginsOn + keyLifeSeconds.
        // - If neither is provided, the key will have no expiration.

        if (endsOn.HasValue && endsOn.Value <= beginsOn)
            throw new ArgumentException("endsOn must be after beginsOn", nameof(endsOn));

        long? effectiveKeyLifeSeconds = null;
        if (endsOn.HasValue)
        {
            // compute seconds difference and ensure it's positive
            var secs = (long)Math.Floor((endsOn.Value - beginsOn).TotalSeconds);
            if (secs <= 0)
                throw new ArgumentException("endsOn must be at least one second after beginsOn", nameof(endsOn));

            effectiveKeyLifeSeconds = secs;
        }
        else if (keyLifeSeconds.HasValue)
        {
            effectiveKeyLifeSeconds = keyLifeSeconds.Value;
            endsOn = beginsOn.AddSeconds(effectiveKeyLifeSeconds.Value);
        }

        // Use the provided beginsOn as the creation time for generated PgpKeyPair objects.
        DateTime creationTime = beginsOn;

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
            creationTime);

        var masterSubpacket = new PgpSignatureSubpacketGenerator();
        masterSubpacket.SetKeyFlags(true, PgpKeyFlags.CanSign | PgpKeyFlags.CanCertify);
        masterSubpacket.SetPreferredHashAlgorithms(true, ToIntArray(HashAlgorithmTag.Sha512, HashAlgorithmTag.Sha256));
        masterSubpacket.SetPreferredSymmetricAlgorithms(true,
            ToIntArray(SymmetricKeyAlgorithmTag.Aes256, SymmetricKeyAlgorithmTag.Aes192,
                SymmetricKeyAlgorithmTag.Aes128));
        masterSubpacket.SetFeature(true, Features.FEATURE_MODIFICATION_DETECTION);
        if (effectiveKeyLifeSeconds.HasValue) masterSubpacket.SetKeyExpirationTime(true, effectiveKeyLifeSeconds.Value);

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
        var encPgpKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaEncrypt, encKeyPair, creationTime);

        var encSubpacket = new PgpSignatureSubpacketGenerator();
        encSubpacket.SetKeyFlags(true, PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage);
        if (effectiveKeyLifeSeconds.HasValue) encSubpacket.SetKeyExpirationTime(true, effectiveKeyLifeSeconds.Value);

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
