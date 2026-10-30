using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PracticalTools.GPG.Folders;

namespace PracticalTools.GPG.Preview;

public sealed record PreviewSheet(string Name, string Text)
{
    public override string ToString() => Name;
}

/// <summary>Read-only SpreadsheetML content. No formula evaluation or external resource access.</summary>
internal static class SpreadsheetPreview
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string StrictMain = "http://purl.oclc.org/ooxml/spreadsheetml/main";
    private const string Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictRelations = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";
    private sealed record Relation(string Type, string Target, bool External);

    public static (IReadOnlyList<PreviewSheet> Sheets, bool Truncated) Read(string path, CancellationToken token)
    {
        using var file = File.OpenRead(path);
        ZipDirectoryPreflight.Validate(file, 10_000, token);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("XLSX 包含重复条目。");
        }
        long remainingXml = 32_000_000;
        XDocument ReadXml(string name)
        {
            token.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(name, out var entry)) throw new InvalidDataException($"XLSX 缺少部件：{name}");
            // Count bytes before XML allocation as well as decoded characters inside the reader.
            if (entry.Length > 8_000_000 || entry.Length > remainingXml) throw new InvalidDataException("XLSX XML 部件超过读取限制。");
            using var stream = entry.Open();
            using var bounded = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                if (read > remainingXml || bounded.Length + read > 8_000_000)
                    throw new InvalidDataException("XLSX XML 实际展开数据超过读取限制。");
                remainingXml -= read;
                bounded.Write(buffer, 0, read);
            }
            if (bounded.Length != entry.Length) throw new InvalidDataException("XLSX XML 部件长度与声明不一致。");
            bounded.Position = 0;
            using var reader = XmlReader.Create(bounded, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
            var document = XDocument.Load(reader);
            token.ThrowIfCancellationRequested();
            return document;
        }
        Dictionary<string, Relation> ReadRelations(string part)
        {
            var name = part.Length == 0 ? "_rels/.rels" :
                part[..(part.LastIndexOf('/') + 1)] + "_rels/" + part[(part.LastIndexOf('/') + 1)..] + ".rels";
            var root = ReadXml(name).Root;
            if (root?.Name != Package + "Relationships") throw new InvalidDataException("XLSX 关系表无效。");
            var result = new Dictionary<string, Relation>(StringComparer.Ordinal);
            foreach (var relation in root.Elements(Package + "Relationship"))
            {
                var id = (string?)relation.Attribute("Id") ?? throw new InvalidDataException("关系缺少 ID。");
                var value = new Relation((string?)relation.Attribute("Type") ?? "", (string?)relation.Attribute("Target") ?? "",
                    (string?)relation.Attribute("TargetMode") == "External");
                if (!result.TryAdd(id, value)) throw new InvalidDataException("XLSX 关系 ID 重复。");
            }
            return result;
        }
        string Target(string part, Relation relation)
        {
            if (relation.External) throw new InvalidDataException("工作表和共享字符串必须位于本地 XLSX 包内。");
            var target = Uri.UnescapeDataString(relation.Target);
            if (target.Length == 0 || target.Contains('\\') || target.Contains(':') || target.Contains('?') || target.Contains('#'))
                throw new InvalidDataException("XLSX 部件路径无效。");
            var combined = target.StartsWith('/') ? target[1..] : part[..(part.LastIndexOf('/') + 1)] + target;
            var segments = new List<string>();
            foreach (var segment in combined.Split('/'))
            {
                if (segment == ".") continue;
                if (segment == "..")
                { if (segments.Count == 0) throw new InvalidDataException("XLSX 部件路径越界。"); segments.RemoveAt(segments.Count - 1); }
                else if (segment.Length == 0) throw new InvalidDataException("XLSX 部件路径无效。");
                else segments.Add(segment);
            }
            return string.Join('/', segments);
        }
        static bool IsType(Relation relation, string type) => relation.Type == Relations + "/" + type || relation.Type == StrictRelations + "/" + type;
        var office = ReadRelations("").Values.Where(r => IsType(r, "officeDocument")).ToList();
        if (office.Count != 1) throw new InvalidDataException("XLSX 工作簿关系无效。");
        var workbookPath = Target("", office[0]);
        var workbook = ReadXml(workbookPath).Root;
        if (workbook == null || workbook.Name.LocalName != "workbook" || workbook.Name.NamespaceName is not (Main or StrictMain))
            throw new InvalidDataException("不是受支持的 XLSX 工作簿。");
        var ns = workbook.Name.Namespace;
        XNamespace relNs = ns.NamespaceName == Main ? Relations : StrictRelations;
        var relationships = ReadRelations(workbookPath);
        var shared = new List<string>();
        var stringParts = relationships.Values.Where(r => IsType(r, "sharedStrings")).ToList();
        if (stringParts.Count > 1) throw new InvalidDataException("共享字符串部件重复。");
        if (stringParts.Count == 1)
        {
            var root = ReadXml(Target(workbookPath, stringParts[0])).Root;
            if (root?.Name != ns + "sst") throw new InvalidDataException("共享字符串表无效。");
            foreach (var item in root.Elements(ns + "si"))
            {
                token.ThrowIfCancellationRequested();
                if (shared.Count >= 100_000) throw new InvalidDataException("共享字符串数量超过限制。");
                shared.Add(RichText(item, ns));
            }
        }
        var sheets = workbook.Element(ns + "sheets")?.Elements(ns + "sheet").ToList()
            ?? throw new InvalidDataException("XLSX 缺少工作表列表。");
        if (sheets.Count == 0) throw new InvalidDataException("XLSX 没有工作表。");
        var output = new List<PreviewSheet>();
        bool truncated = sheets.Count > 20;
        int cells = 0, characters = 0;
        foreach (var sheet in sheets.Take(20))
        {
            token.ThrowIfCancellationRequested();
            if (cells >= 50_000 || characters >= PreviewService.MaxCharacters) { truncated = true; break; }
            var id = (string?)sheet.Attribute(relNs + "id") ?? "";
            if (!relationships.TryGetValue(id, out var relation)) throw new InvalidDataException("工作表关系缺失。");
            var name = (string?)sheet.Attribute("name") ?? "未命名";
            if (name.Length > 128) throw new InvalidDataException("工作表名称过长。");
            if ((string?)sheet.Attribute("state") is "hidden" or "veryHidden") name += "（隐藏）";
            if (!IsType(relation, "worksheet")) { output.Add(new(name, "此工作表类型不支持内容预览（例如图表工作表）。")); continue; }
            var root = ReadXml(Target(workbookPath, relation)).Root;
            if (root?.Name != ns + "worksheet") throw new InvalidDataException("工作表 XML 无效。");
            var text = new StringBuilder();
            bool sheetTruncated = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int implicitRow = 0;
            foreach (var row in root.Element(ns + "sheetData")?.Elements(ns + "row") ?? [])
            {
                token.ThrowIfCancellationRequested();
                int rowNumber = row.Attribute("r") is { } rowAttribute ? Positive(rowAttribute.Value, 1_048_576) : implicitRow + 1;
                if (rowNumber > 1_048_576) throw new InvalidDataException("工作表行号越界。");
                implicitRow = rowNumber;
                int implicitColumn = 0;
                foreach (var cell in row.Elements(ns + "c"))
                {
                    token.ThrowIfCancellationRequested();
                    var reference = (string?)cell.Attribute("r");
                    var column = reference == null ? implicitColumn + 1 : Column(reference, rowNumber);
                    if (column > 16384) throw new InvalidDataException("单元格列号无效。");
                    implicitColumn = column;
                    reference ??= ColumnName(column) + rowNumber.ToString(CultureInfo.InvariantCulture);
                    if (!seen.Add(reference)) throw new InvalidDataException("工作表单元格位置重复。");
                    if (rowNumber > 2000 || column > 100) { sheetTruncated = true; continue; }
                    if (cells >= 50_000 || characters >= PreviewService.MaxCharacters) { sheetTruncated = true; break; }
                    var value = cell.Element(ns + "v")?.Value;
                    var type = (string?)cell.Attribute("t");
                    bool formulaWithoutValue = cell.Element(ns + "f") != null && string.IsNullOrEmpty(value);
                    string displayed = formulaWithoutValue ? "" : type switch
                    {
                        "s" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < shared.Count
                            ? shared[index] : throw new InvalidDataException("共享字符串索引无效。"),
                        "inlineStr" => RichText(cell.Element(ns + "is"), ns),
                        "b" => value switch { "1" => "TRUE", "0" => "FALSE", _ => throw new InvalidDataException("布尔单元格无效。") },
                        null or "n" or "str" or "e" or "d" => value ?? "",
                        _ => throw new InvalidDataException("不支持的单元格类型。")
                    };
                    if (cell.Element(ns + "f") != null) displayed = formulaWithoutValue ? "[公式无缓存值]" : displayed + " [公式缓存值，未重算]";
                    var line = reference + "\t" + displayed + Environment.NewLine;
                    int count = Math.Min(line.Length, PreviewService.MaxCharacters - characters);
                    text.Append(line.AsSpan(0, count)); characters += count; cells++;
                    if (count < line.Length) sheetTruncated = true;
                }
                if (cells >= 50_000 || characters >= PreviewService.MaxCharacters) { sheetTruncated = true; break; }
            }
            if (sheetTruncated) { text.AppendLine("[已达到预览范围或容量上限，部分内容未显示。]"); truncated = true; }
            output.Add(new(name, text.Length == 0 ? "空工作表" : text.ToString()));
        }
        return (output, truncated);
    }

    private static string RichText(XElement? item, XNamespace ns) => item == null ? "" : string.Concat(
        item.Elements().SelectMany(e => e.Name == ns + "t" ? new[] { e.Value } :
            e.Name == ns + "r" ? e.Elements(ns + "t").Select(t => t.Value) : []));
    private static int Positive(string value, int max) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number > 0 && number <= max ? number : throw new InvalidDataException("单元格行号无效。");
    private static int Column(string reference, int row)
    {
        int column = 0, i = 0;
        while (i < reference.Length && reference[i] is >= 'A' and <= 'Z')
        { if (i >= 3) throw new InvalidDataException("单元格地址无效。"); column = column * 26 + reference[i++] - 'A' + 1; }
        if (column == 0 || Positive(reference[i..], 1_048_576) != row) throw new InvalidDataException("单元格地址与行号不一致。");
        return column;
    }
    private static string ColumnName(int column)
    {
        var result = "";
        while (column > 0) { column--; result = (char)('A' + column % 26) + result; column /= 26; }
        return result;
    }
}
