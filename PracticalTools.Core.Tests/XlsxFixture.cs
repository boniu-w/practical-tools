using System.IO.Compression;

namespace PracticalTools.TestFixtures;

internal static class XlsxFixture
{
    public const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public const string Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string Package = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static Dictionary<string, string> Parts(string? cells = null) => new()
    {
        ["[Content_Types].xml"] = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/><Override PartName='/xl/worksheets/data.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'/><Override PartName='/xl/worksheets/empty.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'/><Override PartName='/xl/strings.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml'/></Types>",
        ["_rels/.rels"] = $"<Relationships xmlns='{Package}'><Relationship Id='workbook' Type='{Relations}/officeDocument' Target='xl/workbook.xml'/></Relationships>",
        ["xl/workbook.xml"] = $"<workbook xmlns='{Main}' xmlns:r='{Relations}'><sheets><sheet name='数据' sheetId='2' r:id='data'/><sheet name='空表' sheetId='3' state='hidden' r:id='empty'/></sheets></workbook>",
        ["xl/_rels/workbook.xml.rels"] = $"<Relationships xmlns='{Package}'><Relationship Id='data' Type='{Relations}/worksheet' Target='worksheets/data.xml'/><Relationship Id='empty' Type='{Relations}/worksheet' Target='/xl/worksheets/empty.xml'/><Relationship Id='strings' Type='{Relations}/sharedStrings' Target='strings.xml'/><Relationship Id='external' Type='{Relations}/externalLink' Target='https://invalid.example/never-fetch' TargetMode='External'/></Relationships>",
        ["xl/strings.xml"] = $"<sst xmlns='{Main}'><si><r><t>中文</t></r><r><t>内容</t></r><rPh sb='0' eb='1'><t>不显示拼音</t></rPh></si></sst>",
        ["xl/worksheets/data.xml"] = $"<worksheet xmlns='{Main}'><sheetData>" + (cells ?? "<row r='1'><c r='A1' t='s'><v>0</v></c><c r='C1' t='inlineStr'><is><t>&lt;script&gt;原文&lt;/script&gt;</t></is></c><c r='D1' t='b'><v>1</v></c><c r='E1'><f>1+2</f><v>3</v></c><c r='F1' t='str'><f>NOW()</f></c><c r='G1' t='e'><v>#DIV/0!</v></c><c r='H1' s='1'><v>45292</v></c></row>") + "</sheetData></worksheet>",
        ["xl/worksheets/empty.xml"] = $"<worksheet xmlns='{Main}'><sheetData/></worksheet>"
    };
    public static byte[] Build(Dictionary<string, string>? parts = null, string? duplicate = null)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (path, text) in parts ?? Parts())
            { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(text); }
            if (duplicate != null) { using var writer = new StreamWriter(zip.CreateEntry(duplicate).Open()); writer.Write("<duplicate/>"); }
        }
        return buffer.ToArray();
    }
}
