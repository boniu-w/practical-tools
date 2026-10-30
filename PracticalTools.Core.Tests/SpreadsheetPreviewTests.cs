using Microsoft.VisualStudio.TestTools.UnitTesting;
using PracticalTools.GPG;
using PracticalTools.GPG.Preview;
using PracticalTools.TestFixtures;

namespace PracticalTools.Core.Tests;

[TestClass]
public class SpreadsheetPreviewTests
{
    private static string _public = null!, _secret = null!;
    private string _root = null!;
    [ClassInitialize]
    public static void Keys(TestContext _)
    {
        var pair = GpgHelper.GeneratePgpKeyPair("spreadsheet preview", "password", 2048);
        _public = GpgHelper.ExportPublicKey(pair.pubRing); _secret = GpgHelper.ExportPrivateKey(pair.secRing);
    }
    [TestInitialize] public void Setup() => _root = Directory.CreateTempSubdirectory("pt-xlsx-tests-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);
    private async Task<PreviewContent> Preview(byte[] workbook)
    {
        var source = Path.Combine(_root, "book.xlsx"); File.WriteAllBytes(source, workbook);
        var input = Path.Combine(_root, "renamed.gpg");
        await GpgHelper.EncryptFileByPublicString(source, input, _public);
        try { return await new PreviewService(_root).OpenAsync(input, _secret, "password"); }
        finally { Assert.AreEqual(0, Directory.GetDirectories(_root).Length, "Temporary plaintext must be removed."); }
    }
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorkbookRelationshipsAndValuesAreReadWithoutEvaluatingFormulas(bool strict)
    {
        var parts = XlsxFixture.Parts();
        if (strict) foreach (var name in parts.Keys.ToArray()) parts[name] = parts[name]
            .Replace(XlsxFixture.Main, "http://purl.oclc.org/ooxml/spreadsheetml/main")
            .Replace(XlsxFixture.Relations, "http://purl.oclc.org/ooxml/officeDocument/relationships");
        using var result = await Preview(XlsxFixture.Build(parts));
        Assert.AreEqual("book.xlsx", result.Name); Assert.IsFalse(result.Truncated);
        Assert.AreEqual(2, result.Sheets!.Count);
        var text = result.Sheets[0].Text;
        StringAssert.Contains(text, "A1\t中文内容"); StringAssert.Contains(text, "C1\t<script>原文</script>");
        StringAssert.Contains(text, "D1\tTRUE"); StringAssert.Contains(text, "E1\t3 [公式缓存值，未重算]");
        StringAssert.Contains(text, "F1\t[公式无缓存值]"); StringAssert.Contains(text, "G1\t#DIV/0!");
        StringAssert.Contains(text, "H1\t45292"); Assert.IsFalse(text.Contains("不显示拼音"));
        Assert.AreEqual("空表（隐藏）", result.Sheets[1].Name); Assert.AreEqual("空工作表", result.Sheets[1].Text);
    }
    [TestMethod]
    public async Task SparseCoordinatesAndImplicitCellsArePreserved()
    {
        var parts = XlsxFixture.Parts("<row><c><v>1</v></c><c r='C1'><v>3</v></c><c><v>4</v></c></row><row r='100'><c r='Z100'><v>100</v></c></row>");
        using var result = await Preview(XlsxFixture.Build(parts));
        StringAssert.Contains(result.Sheets![0].Text, "D1\t4"); StringAssert.Contains(result.Sheets[0].Text, "Z100\t100");
    }
    [DataTestMethod]
    [DataRow("range")]
    [DataRow("characters")]
    [DataRow("sheets")]
    [DataRow("cells")]
    public async Task LimitsAreReportedAsPartialPreview(string kind)
    {
        var parts = XlsxFixture.Parts();
        if (kind == "range") parts = XlsxFixture.Parts("<row r='1'><c r='CW1'><v>outside</v></c></row><row r='2001'><c r='A2001'><v>outside</v></c></row>");
        if (kind == "characters") parts = XlsxFixture.Parts("<row><c t='inlineStr'><is><t>" + new string('x', PreviewService.MaxCharacters + 1) + "</t></is></c></row>");
        if (kind == "sheets") parts["xl/workbook.xml"] = $"<workbook xmlns='{XlsxFixture.Main}' xmlns:r='{XlsxFixture.Relations}'><sheets>" +
            string.Concat(Enumerable.Range(1, 21).Select(i => $"<sheet name='Sheet{i}' r:id='empty'/>")) + "</sheets></workbook>";
        if (kind == "cells")
        {
            // Empty cells reach the cell-count cap before the character cap only with short addresses;
            // repeated sheets share the global budget, whichever limit is reached first.
            var rows = string.Concat(Enumerable.Range(1, 2000).Select(i => $"<row r='{i}'>" + string.Concat(Enumerable.Repeat("<c/>", 100)) + "</row>"));
            parts = XlsxFixture.Parts(rows);
        }
        using var result = await Preview(XlsxFixture.Build(parts)); Assert.IsTrue(result.Truncated);
        if (kind == "sheets") Assert.AreEqual(20, result.Sheets!.Count);
        if (kind == "range") Assert.IsFalse(result.Sheets![0].Text.Contains("outside"));
        Assert.IsTrue(result.Sheets!.Sum(s => s.Text.Length) < PreviewService.MaxCharacters + 2000);
    }
    [DataTestMethod]
    [DataRow("external")]
    [DataRow("traversal")]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("index")]
    [DataRow("address")]
    [DataRow("cell-duplicate")]
    [DataRow("dtd")]
    [DataRow("xml-limit")]
    public async Task InvalidOrOversizedWorkbooksFailAndCleanPlaintext(string kind)
    {
        var parts = XlsxFixture.Parts();
        if (kind == "external") parts["xl/_rels/workbook.xml.rels"] = parts["xl/_rels/workbook.xml.rels"].Replace("Target='worksheets/data.xml'", "Target='https://invalid.example/sheet.xml' TargetMode='External'");
        if (kind == "traversal") parts["xl/_rels/workbook.xml.rels"] = parts["xl/_rels/workbook.xml.rels"].Replace("worksheets/data.xml", "../../escape.xml");
        if (kind == "missing") parts.Remove("xl/worksheets/data.xml");
        if (kind == "index") parts["xl/strings.xml"] = $"<sst xmlns='{XlsxFixture.Main}'/>";
        if (kind == "address") parts = XlsxFixture.Parts("<row r='1'><c r='A2'><v>1</v></c></row>");
        if (kind == "cell-duplicate") parts = XlsxFixture.Parts("<row r='1'><c r='A1'/><c r='A1'/></row>");
        if (kind == "dtd") parts["xl/strings.xml"] = "<!DOCTYPE sst [<!ENTITY external SYSTEM 'file:///missing'>]>" + parts["xl/strings.xml"];
        if (kind == "xml-limit") parts["xl/strings.xml"] = $"<sst xmlns='{XlsxFixture.Main}'><si><t>" + new string('x', 4_000_001) + "</t></si></sst>";
        Exception? failure = null;
        try { using var result = await Preview(XlsxFixture.Build(parts, kind == "duplicate" ? "xl/workbook.xml" : null)); }
        catch (Exception ex) { failure = ex; }
        Assert.IsTrue(failure is InvalidDataException or System.Xml.XmlException, failure?.ToString() ?? "Expected rejection.");
    }
}
