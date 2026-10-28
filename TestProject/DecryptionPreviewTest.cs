using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeOpenXml;
using Org.BouncyCastle.Bcpg.OpenPgp;
using PracticalTools.GPG;

namespace TestProject1;

[TestClass]
public class DecryptionPreviewTest
{
    private static GpgFile _key = null!;
    private string _directory = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        var (pub, sec, _, _) = GpgHelper.GeneratePgpKeyPair("preview-test@example.invalid", "preview-tests");
        _key = new GpgFile
        {
            PublicKey = GpgHelper.ExportPublicKey(pub),
            SecretKey = GpgHelper.ExportPrivateKey(sec),
            Passphrase = "preview-tests"
        };
    }

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PracticalToolsPreviewTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public async Task PreviewDecryptsRenamedVideoAndDeletesTemporaryCopy()
    {
        string original = Path.Combine(_directory, "中文 video.mp4");
        byte[] bytes = RandomNumberGenerator.GetBytes(8 * 1024 * 1024);
        await File.WriteAllBytesAsync(original, bytes);
        string encrypted = original + ".gpg";
        await GpgHelper.EncryptFileByPublicString(original, encrypted, _key.PublicKey);
        string renamed = Path.Combine(_directory, "renamed.gpg");
        File.Move(encrypted, renamed);

        string temporaryDirectory;
        using (var preview = await DecryptedPreviewSession.CreateAsync(renamed, _key))
        {
            temporaryDirectory = preview.DirectoryPath;
            Assert.AreEqual("中文 video.mp4", preview.OriginalFileName);
            Assert.AreEqual(".mp4", Path.GetExtension(preview.FilePath));
            CollectionAssert.AreEqual(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(preview.FilePath)));
        }
        Assert.IsFalse(Directory.Exists(temporaryDirectory));
        Assert.IsTrue(File.Exists(renamed));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(original));
    }

    [TestMethod]
    public async Task PreviewRejectsWrongPassphraseAndCorruptCiphertext()
    {
        string input = Path.Combine(_directory, "sample.txt");
        await File.WriteAllTextAsync(input, "预览完整性校验");
        string encrypted = input + ".gpg";
        await GpgHelper.EncryptFileByPublicString(input, encrypted, _key.PublicKey);
        var incorrectKey = new GpgFile { SecretKey = _key.SecretKey, Passphrase = "incorrect" };
        await Assert.ThrowsExceptionAsync<PgpException>(() => DecryptedPreviewSession.CreateAsync(encrypted, incorrectKey));

        byte[] bytes = await File.ReadAllBytesAsync(encrypted);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(encrypted, bytes);
        await Assert.ThrowsExceptionAsync<PgpException>(() => DecryptedPreviewSession.CreateAsync(encrypted, _key));
    }

    [TestMethod]
    public async Task PreviewHonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            DecryptedPreviewSession.CreateAsync("not-opened.gpg", _key, cts.Token));
    }

    [TestMethod]
    public void TextPreviewEscapesHtmlAndSupportsChineseEncodings()
    {
        string input = Path.Combine(_directory, "sample.html");
        File.WriteAllText(input, "<script>alert('x')</script>中文正文", new UTF8Encoding(false));
        string html = PreviewContentBuilder.Build(input, "<unsafe>.html");
        StringAssert.Contains(html, "&lt;script&gt;");
        StringAssert.Contains(html, "中文正文");
        Assert.IsFalse(html.Contains("<script>alert"));

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(input, "简体中文内容", Encoding.GetEncoding(936));
        StringAssert.Contains(PreviewContentBuilder.Build(input, "sample.txt"), "简体中文内容");
        File.WriteAllText(input, "Unicode 正文", Encoding.Unicode);
        StringAssert.Contains(PreviewContentBuilder.Build(input, "sample.txt"), "Unicode 正文");
    }

    [TestMethod]
    public void WordPreviewKeepsParagraphsAndTables()
    {
        string input = Path.Combine(_directory, "sample.docx");
        using (var zip = ZipFile.Open(input, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                <w:p><w:r><w:t>Word 正文 &lt;内容&gt;</w:t></w:r></w:p>
                <w:tbl><w:tr><w:tc><w:p><w:r><w:t>表格内容</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                </w:body></w:document>
                """);
        string html = PreviewContentBuilder.Build(input, "sample.docx");
        StringAssert.Contains(html, "Word 正文 &lt;内容&gt;");
        StringAssert.Contains(html, "<td>表格内容</td>");
    }

    [TestMethod]
    public void ExcelPreviewShowsMultipleSheetsAndCachedValues()
    {
        string input = Path.Combine(_directory, "sample.xlsx");
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using (var package = new ExcelPackage())
        {
            var first = package.Workbook.Worksheets.Add("中文工作表");
            first.Cells[1, 1].Value = "<单元格>";
            first.Cells[2, 2].Value = 123;
            package.Workbook.Worksheets.Add("空表");
            package.SaveAs(new FileInfo(input));
        }
        string html = PreviewContentBuilder.Build(input, "sample.xlsx");
        StringAssert.Contains(html, "中文工作表");
        StringAssert.Contains(html, "&lt;单元格&gt;");
        StringAssert.Contains(html, "<td>123</td>");
        StringAssert.Contains(html, "空工作表");
    }

    [TestMethod]
    public void LargeTextIsTruncatedAndBinaryFilesAreRejected()
    {
        string input = Path.Combine(_directory, "large.txt");
        File.WriteAllText(input, new string('a', PreviewContentBuilder.MaxTextCharacters + 20));
        StringAssert.Contains(PreviewContentBuilder.Build(input, "large.txt"), "仅预览前 200 万个字符");
        File.WriteAllBytes(input, [0, 1, 2, 3]);
        Assert.ThrowsException<NotSupportedException>(() => PreviewContentBuilder.Build(input, "binary.bin"));
    }

    [TestMethod]
    public async Task ExistingDecryptEntryPointStillWorks()
    {
        string input = Path.Combine(_directory, "sample.txt");
        File.WriteAllText(input, "兼容现有解密入口");
        await GpgHelper.EncryptFileByPublicString(input, input + ".gpg", _key.PublicKey);
        string output = Path.Combine(_directory, "output.txt");
        await GpgHelper.DecryptFileByPublicString(input + ".gpg", output, _key.SecretKey, _key.Passphrase);
        Assert.AreEqual(File.ReadAllText(input), File.ReadAllText(output));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreviewReadsCompressedPgpAndAsciiArmor(bool armored)
    {
        string input = Path.Combine(_directory, "压缩内容.txt");
        await File.WriteAllTextAsync(input, "压缩 PGP 文件正文");
        string encrypted = input + ".gpg";
        GpgHelper.EncryptFile(encrypted, input, _key.PublicKey, armor: false);
        if (armored)
        {
            string armorPath = Path.Combine(_directory, "armored.gpg");
            using (var output = File.Create(armorPath))
            using (var armor = new Org.BouncyCastle.Bcpg.ArmoredOutputStream(output))
            using (var source = File.OpenRead(encrypted))
                source.CopyTo(armor);
            encrypted = armorPath;
        }
        using var preview = await DecryptedPreviewSession.CreateAsync(encrypted, _key);
        Assert.AreEqual("压缩 PGP 文件正文", await File.ReadAllTextAsync(preview.FilePath));
    }
}
