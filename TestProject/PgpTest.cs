using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Org.BouncyCastle.Bcpg.OpenPgp;
using PracticalTools.GPG;
using PracticalTools.util;

namespace TestProject1;

[TestClass]
public class PgpTest
{
    /// <summary>
    /// 测试前自动探测 gpg.exe 路径（仅测试需要）。
    /// </summary>
    [ClassInitialize]
    public static void Setup(TestContext _)
    {
        GpgHelper.GpgExePath = ResolveGpgPath();
    }

    private static string? ResolveGpgPath()
    {
        string[] candidates =
        {
            @"C:\Program Files (x86)\gnupg\bin\gpg.exe",
            @"C:\Program Files\Git\usr\bin\gpg.exe",
            @"C:\Program Files\GnuPG\bin\gpg.exe",
            @"gpg"
        };

        foreach (var path in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(2000);
                    if (p.ExitCode == 0) return path;
                }
            }
            catch
            {
                // try next
            }
        }

        return null;
    }

    [TestMethod]
    public void TestPgp()
    {
        string identity = "boniu-w@live.cn";
        string passphrase = "Soccer0boy$";
        int keySize = 2048;
        long validDuration = long.MaxValue;
        var (pubRing, secRing, beginsOn, endOn) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, keySize);

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
        string message = "Hello, this is a EncryptFile message.";

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

    [TestMethod]
    public void TestSetKeyExpiration()
    {
        // GpgHelper.GpgExePath 已自动探测，无需手动设置

        // 使用临时 GNUPGHOME 隔离，避免污染用户密钥环
        string tempHome = Path.Combine(Path.GetTempPath(), $"gpgtmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempHome);

        try
        {
            // 1. 生成密钥对
            string identity = "test-expire@example.com";
            string passphrase = "Test1234!";
            var (pubRing, secRing, beginsOn, endsOn) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, 2048);

            string pubKey = GpgHelper.ExportPublicKey(pubRing);
            string secKey = GpgHelper.ExportPrivateKey(secRing);
            Console.WriteLine("生成密钥完成，初始过期时间: " + (endsOn?.ToString() ?? "永不过期"));

            // 2. 导入到临时 GPG 密钥环
            ImportToGpg(pubKey, tempHome);
            ImportToGpg(secKey, tempHome);
            Console.WriteLine("已导入密钥到临时密钥环");

            // 3. 获取指纹
            string fingerprint = GetKeyFingerprint(identity, tempHome);
            Console.WriteLine("密钥指纹: " + fingerprint);
            Assert.IsNotNull(fingerprint, "未能获取密钥指纹");

            // 4. 修改过期时间为 1 年（需要传密码）
            RunGpgWithHome(tempHome,
                $"--batch --yes --pinentry-mode loopback --passphrase \"{passphrase}\" --quick-set-expire \"{fingerprint}\" \"1y\"");
            Console.WriteLine("已将过期时间改为 1 年");

            // 5. 验证
            string keyInfo = RunGpgWithHome(tempHome, $"--list-keys \"{identity}\"");
            Console.WriteLine("修改后的密钥信息:\n" + keyInfo);
            Assert.IsTrue(keyInfo.Contains("[expires:"), "密钥应有过期时间标记");

            // 6. 再改为永不过期
            RunGpgWithHome(tempHome,
                $"--batch --yes --pinentry-mode loopback --passphrase \"{passphrase}\" --quick-set-expire \"{fingerprint}\" \"0\"");
            keyInfo = RunGpgWithHome(tempHome, $"--list-keys \"{identity}\"");
            Console.WriteLine("改为永不过期后的信息:\n" + keyInfo);
            Assert.IsFalse(keyInfo.Contains("[expires:"), "过期时间应已移除");
        }
        finally
        {
            try { Directory.Delete(tempHome, true); }
            catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void TestGetKeyFingerprints()
    {
        // GpgHelper.GpgExePath 已自动探测，无需手动设置

        // 1. 查询所有密钥指纹（不抛异常即为通过）
        string[] all = GpgHelper.GetKeyFingerprints();
        Console.WriteLine($"本地密钥环共有 {all.Length} 个密钥");
        foreach (var fpr in all)
            Console.WriteLine($"  {fpr}");
        Assert.IsNotNull(all);

        // 2. 用不存在的标识查询，应返回空数组
        string[] none = GpgHelper.GetKeyFingerprints("no-such-key@nowhere.com");
        Console.WriteLine($"匹配 'no-such-key' 的密钥数: {none.Length}");
        Assert.AreEqual(0, none.Length);

        // 3. 生成临时密钥导入，验证能查到
        string tempHome = Path.Combine(Path.GetTempPath(), $"gpgtmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempHome);

        try
        {
            string identity = "test-fpr@example.com";
            var (pubRing, secRing, _, _) = GpgHelper.GeneratePgpKeyPair(identity, "Test1234!", 2048);

            ImportToGpg(GpgHelper.ExportPublicKey(pubRing), tempHome);
            ImportToGpg(GpgHelper.ExportPrivateKey(secRing), tempHome);

            // 从临时密钥环直接查（GpgHelper 方法走默认 keyring，这里复用 RunGpgWithHome）
            string output = RunGpgWithHome(tempHome, $"--list-keys --with-colons \"{identity}\"");
            Console.WriteLine("临时密钥环查询结果:\n" + output);

            // 验证 fpr 行存在
            Assert.IsTrue(output.Contains("fpr:"), "应包含指纹行");

            // 解析出指纹并验证格式（40 位 hex）
            foreach (var line in output.Split('\n'))
            {
                if (line.Trim().StartsWith("fpr:"))
                {
                    var fpr = line.Trim().Split(':')[9];
                    Console.WriteLine($"解析到指纹: {fpr}");
                    Assert.AreEqual(40, fpr.Length, "指纹应为 40 位 hex");
                    Assert.IsTrue(fpr.All(c => "0123456789ABCDEFabcdef".Contains(c)), "指纹应全为 hex 字符");
                    break;
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempHome, true); } catch { }
        }
    }

    private static void ImportToGpg(string armoredKey, string gnupgHome)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GpgHelper.GpgExePath,
            Arguments = "--batch --yes --import",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // MSYS2: 禁止自动转换参数和环境变量；GNUPGHOME 用 MSYS2 格式 /c/Users/...
        psi.Environment["MSYS2_ARG_CONV_EXCL"] = "*";
        psi.Environment["MSYS2_ENV_CONV_EXCL"] = "*";
        psi.Environment["GNUPGHOME"] = WindowsPathToMsys2(gnupgHome);

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Write(armoredKey);
        process.StandardInput.Close();
        process.WaitForExit(30_000);

        if (process.ExitCode != 0)
            throw new Exception($"GPG import failed: {process.StandardError.ReadToEnd()}");
    }

    /// <summary>C:\Users\... → /c/Users/...</summary>
    private static string WindowsPathToMsys2(string winPath)
    {
        if (winPath.Length >= 2 && winPath[1] == ':')
            return "/" + char.ToLower(winPath[0]) + winPath[2..].Replace('\\', '/');
        return winPath.Replace('\\', '/');
    }

    private static string GetKeyFingerprint(string identity, string gnupgHome)
    {
        string output = RunGpgWithHome(gnupgHome, $"--list-keys --with-colons \"{identity}\"");
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("fpr:"))
            {
                var parts = line.Split(':');
                if (parts.Length > 9) return parts[9];
            }
        }
        return null!;
    }

    private static string RunGpgWithHome(string gnupgHome, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GpgHelper.GpgExePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // MSYS2: 禁止自动转换参数和环境变量；GNUPGHOME 用 MSYS2 格式 /c/Users/...
        psi.Environment["MSYS2_ARG_CONV_EXCL"] = "*";
        psi.Environment["MSYS2_ENV_CONV_EXCL"] = "*";
        psi.Environment["GNUPGHOME"] = WindowsPathToMsys2(gnupgHome);

        using var process = new Process { StartInfo = psi };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);

        if (process.ExitCode != 0)
            throw new Exception($"GPG error (exit {process.ExitCode}): {error}");

        return output;
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