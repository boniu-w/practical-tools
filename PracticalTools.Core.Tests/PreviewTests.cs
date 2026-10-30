using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.GPG;
using PracticalTools.GPG.Preview;

namespace PracticalTools.Core.Tests;

[TestClass]
public class PreviewTests
{
    private string _root = null!;
    private static string _public = null!, _secret = null!;
    [ClassInitialize]
    public static void Keys(TestContext _)
    {
        var pair = GpgHelper.GeneratePgpKeyPair("preview", "password", 2048);
        _public = GpgHelper.ExportPublicKey(pair.pubRing); _secret = GpgHelper.ExportPrivateKey(pair.secRing);
    }
    [TestInitialize] public void Setup() => _root = Directory.CreateTempSubdirectory("pt-preview-tests-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);
    private async Task<string> Encrypt(string name, byte[] data)
    {
        var source = Path.Combine(_root, name); File.WriteAllBytes(source, data);
        var encrypted = Path.Combine(_root, "renamed.gpg");
        await GpgHelper.EncryptFileByPublicString(source, encrypted, _public); return encrypted;
    }
    private void Clean() => Assert.AreEqual(0, Directory.GetDirectories(_root).Length);
    [TestMethod]
    public async Task RenamedCipherUsesLiteralNameAndReturnsPassiveTextWithNoPlaintextLeft()
    {
        const string original = "中文 <script>alert(1)</script>";
        var input = await Encrypt("内容.html", System.Text.Encoding.UTF8.GetBytes(original));
        using var result = await new PreviewService(_root).OpenAsync(input, _secret, "password");
        Assert.AreEqual("内容.html", result.Name); Assert.AreEqual(original, result.Text); Clean();
    }
    [TestMethod]
    public async Task TextIsTruncated()
    {
        var input = await Encrypt("large.txt", new string('x', PreviewService.MaxCharacters + 100).Select(c => (byte)c).ToArray());
        using var result = await new PreviewService(_root).OpenAsync(input, _secret, "password");
        Assert.IsTrue(result.Truncated); Assert.AreEqual(PreviewService.MaxCharacters, result.Text!.Length); Clean();
    }
    [TestMethod]
    public async Task ImageBufferIsClearedOnDispose()
    {
        var input = await Encrypt("image.png", [1, 2, 3]);
        var result = await new PreviewService(_root).OpenAsync(input, _secret, "password");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Image!); Clean();
        result.Dispose(); Assert.IsTrue(result.Image!.All(b => b == 0));
    }
    [DataTestMethod]
    [DataRow("password")]
    [DataRow("limit")]
    [DataRow("cancel")]
    [DataRow("tamper")]
    [DataRow("unsupported")]
    public async Task FailedOperationsLeaveNoWorkspace(string kind)
    {
        var input = await Encrypt(kind == "unsupported" ? "file.pdf" : "file.txt", [65, 66, 67]);
        if (kind == "tamper") { var bytes = File.ReadAllBytes(input); bytes[^1] ^= 1; File.WriteAllBytes(input, bytes); }
        using var cts = new CancellationTokenSource(); if (kind == "cancel") cts.Cancel();
        Exception? failure = null;
        try { using var result = await new PreviewService(_root, kind == "limit" ? 2 : 1024).OpenAsync(input, _secret, kind == "password" ? "wrong" : "password", cts.Token); }
        catch (Exception ex) { failure = ex; }
        Assert.IsNotNull(failure); Clean();
    }
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WordReadsTextAndRejectsDtd(bool dtd)
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write((dtd ? "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///missing'>]>" : "") +
                "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>正文</w:t></w:r></w:p></w:body></w:document>");
        var input = await Encrypt("word.docx", data.ToArray());
        if (dtd) await Assert.ThrowsExceptionAsync<System.Xml.XmlException>(() => new PreviewService(_root).OpenAsync(input, _secret, "password"));
        else { using var result = await new PreviewService(_root).OpenAsync(input, _secret, "password"); Assert.AreEqual("正文" + Environment.NewLine, result.Text); }
        Clean();
    }
}
