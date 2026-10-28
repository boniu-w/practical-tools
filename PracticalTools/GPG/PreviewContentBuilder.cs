using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using OfficeOpenXml;

namespace PracticalTools.GPG;

/// <summary>将文档内容转换成仅用于本地查看的 HTML；所有原文均转义，不运行文件中的脚本。</summary>
public static class PreviewContentBuilder
{
    public const int MaxTextCharacters = 2_000_000;
    public const int MaxSpreadsheetRows = 2000;
    public const int MaxSpreadsheetColumns = 100;
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string Build(string filePath, string originalFileName, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string nonce = Guid.NewGuid().ToString("N");
        string source = Encode(Path.GetFileName(filePath));
        string body = extension switch
        {
            ".mp4" or ".m4v" or ".webm" or ".ogv" or ".mov" or ".avi" or ".mkv" or ".wmv" =>
                $"<div class='media'><video controls autoplay preload='metadata' src='{source}'></video></div>" +
                "<p class='hint'>支持播放、暂停、拖动进度和音量调节。若无法播放，可能是当前系统不支持该视频编码。</p>",
            ".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac" or ".aac" =>
                $"<audio controls src='{source}'></audio>",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".avif" =>
                $"<div class='media'><img alt='图片预览' src='{source}'></div>",
            ".docx" => ReadWord(filePath, token),
            ".xlsx" => ReadExcel(filePath, token),
            ".doc" or ".xls" => throw new NotSupportedException("旧版 .doc / .xls 暂不支持内嵌预览，请先转为 .docx / .xlsx 后加密。"),
            ".zip" or ".7z" or ".rar" or ".exe" or ".dll" =>
                throw new NotSupportedException("该文件类型暂不支持内容预览，请使用“我要解密”保存后查看。"),
            _ => ReadText(filePath)
        };
        token.ThrowIfCancellationRequested();
        return $$"""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'; script-src 'nonce-{{nonce}}'; connect-src 'none'; base-uri 'none'; form-action 'none'">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{Encode(originalFileName)}}</title><style>
            body{font:15px/1.65 'Microsoft YaHei',sans-serif;margin:20px;color:#263238;background:#fff}
            h1{font-size:17px;margin:0 0 14px;overflow-wrap:anywhere}h2{font-size:16px}
            pre{white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.6 Consolas,'Microsoft YaHei',monospace}
            table{border-collapse:collapse;white-space:pre-wrap}td,th{border:1px solid #dce2e7;padding:5px 10px;min-width:50px;max-width:500px;overflow-wrap:anywhere}
            th{background:#edf3f8;position:sticky;top:0}p{white-space:pre-wrap}.hint{color:#62727b;font-size:12px}
            .media{text-align:center;background:#111;border-radius:4px}.media video{width:100%;max-height:70vh;display:block}
            img{max-width:100%;height:auto}audio{width:100%}details{margin-bottom:18px}summary{cursor:pointer;font-weight:bold}
            .sheet{overflow:auto;max-height:70vh;margin-top:8px}
            </style></head><body><h1>{{Encode(originalFileName)}}</h1>{{body}}
            <script nonce="{{nonce}}">document.querySelectorAll('video,audio,img').forEach(function(el){
            el.addEventListener('error',function(){window.chrome.webview.postMessage('media-error');});
            if(el.error || (el.tagName === 'IMG' && el.complete && !el.naturalWidth)) window.chrome.webview.postMessage('media-error');
            });</script></body></html>
            """;
    }

    private static string ReadText(string path)
    {
        string text;
        try { text = ReadBoundedText(path, new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = ReadBoundedText(path, Encoding.GetEncoding(54936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
        }
        if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t' and not '\f'))
            throw new NotSupportedException("该文件不是可识别的文本，暂不支持此格式的预览。支持文本、图片、PDF、DOCX、XLSX 和常见音视频。" );
        bool truncated = text.Length > MaxTextCharacters;
        return (truncated ? "<p class='hint'>文件较大，仅预览前 200 万个字符。</p>" : "") +
            "<pre>" + Encode(truncated ? text[..MaxTextCharacters] : text) + "</pre>";
    }

    private static string ReadBoundedText(string path, Encoding encoding)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        char[] chars = new char[MaxTextCharacters + 1];
        int read = reader.ReadBlock(chars, 0, chars.Length);
        return new string(chars, 0, read);
    }

    private static string ReadWord(string path, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("Word 文件缺少正文。" );
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 20_000_000
        });
        var document = XDocument.Load(reader);
        var body = document.Root?.Element(Word + "body") ?? throw new InvalidDataException("无法读取 Word 正文。" );
        var html = new StringBuilder("<p class='hint'>Word 内容预览（文字与表格，不还原完整分页、图片和版式）。</p>");
        foreach (var element in body.Elements())
        {
            token.ThrowIfCancellationRequested();
            if (html.Length >= MaxTextCharacters)
            {
                html.Append("<p class='hint'>内容较多，已截断预览。</p>");
                break;
            }
            if (element.Name == Word + "p") html.Append("<p>").Append(Encode(WordText(element))).Append("</p>");
            else if (element.Name == Word + "tbl")
            {
                html.Append("<table>");
                foreach (var row in element.Elements(Word + "tr"))
                {
                    token.ThrowIfCancellationRequested();
                    if (html.Length >= MaxTextCharacters) break;
                    html.Append("<tr>");
                    foreach (var cell in row.Elements(Word + "tc"))
                        html.Append("<td>").Append(Encode(string.Join("\n",
                            cell.Elements(Word + "p").Select(WordText)))).Append("</td>");
                    html.Append("</tr>");
                }
                html.Append("</table>");
            }
        }
        return html.ToString();
    }

    private static string WordText(XElement element) => string.Concat(element.Descendants().Select(node =>
        node.Name == Word + "t" ? node.Value :
        node.Name == Word + "tab" ? "\t" :
        node.Name == Word + "br" || node.Name == Word + "cr" ? "\n" : ""));

    private static string ReadExcel(string path, CancellationToken token)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var workbook = new ExcelPackage(new FileInfo(path));
        var html = new StringBuilder("<p class='hint'>点击工作表标题展开或收起。每张表最多显示 2000 行、100 列；显示单元格内容，不执行宏或重算公式。</p>");
        int cells = 0;
        foreach (var sheet in workbook.Workbook.Worksheets)
        {
            token.ThrowIfCancellationRequested();
            if (cells >= 50_000 || html.Length >= MaxTextCharacters)
            {
                html.Append("<p class='hint'>工作簿内容较多，剩余内容未显示，请使用“我要解密”查看完整文件。</p>");
                break;
            }
            html.Append("<details open><summary>").Append(Encode(sheet.Name)).Append("</summary>");
            if (sheet.Dimension == null) { html.Append("<p>空工作表</p></details>"); continue; }
            int firstRow = sheet.Dimension.Start.Row, firstColumn = sheet.Dimension.Start.Column;
            int lastRow = Math.Min(sheet.Dimension.End.Row, firstRow + MaxSpreadsheetRows - 1);
            int lastColumn = Math.Min(sheet.Dimension.End.Column, firstColumn + MaxSpreadsheetColumns - 1);
            html.Append("<div class='sheet'><table><tr><th></th>");
            for (int col = firstColumn; col <= lastColumn; col++)
                html.Append("<th>").Append(ExcelCellAddress.GetColumnLetter(col)).Append("</th>");
            html.Append("</tr>");
            for (int row = firstRow; row <= lastRow; row++)
            {
                token.ThrowIfCancellationRequested();
                if (cells >= 50_000 || html.Length >= MaxTextCharacters)
                {
                    html.Append("<tr><td>已达到工作簿预览上限。</td></tr>");
                    break;
                }
                html.Append("<tr><th>").Append(row).Append("</th>");
                for (int col = firstColumn; col <= lastColumn; col++)
                {
                    html.Append("<td>").Append(Encode(sheet.Cells[row, col].Text)).Append("</td>");
                    cells++;
                }
                html.Append("</tr>");
            }
            html.Append("</table></div>");
            if (lastRow < sheet.Dimension.End.Row || lastColumn < sheet.Dimension.End.Column)
                html.Append("<p class='hint'>此工作表超过预览上限，部分行或列未显示。</p>");
            html.Append("</details>");
        }
        return html.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
