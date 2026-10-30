using System.IO.Compression;
using System.Text;
using System.Xml;
using PracticalTools.GPG.Folders;

namespace PracticalTools.GPG.Preview;

public sealed record PreviewContent(string Name, string? Text, byte[]? Image, bool Truncated) : IDisposable
{
    public IReadOnlyList<PreviewSheet>? Sheets { get; init; }
    public byte[]? Pdf { get; init; }
    public void Dispose() { if (Image != null) Array.Clear(Image); if (Pdf != null) Array.Clear(Pdf); }
}

/// <summary>Produces bounded, passive content; temporary plaintext is removed before returning.</summary>
public sealed class PreviewService(string workspaceParent, long maxBytes = 64 * 1024 * 1024)
{
    public const int MaxCharacters = 200_000;
    public Task<PreviewContent> OpenAsync(string input, string secret, string password, CancellationToken token = default) =>
        Task.Run(() => Open(input, secret, password, token), token);

    private PreviewContent Open(string input, string secret, string password, CancellationToken token)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var workspace = new PrivateWorkspace(workspaceParent);
        var payload = Path.Combine(workspace.Root, "payload");
        var name = GpgHelper.DecryptFileForArchive(input, payload, secret, password, token, null, maxBytes);
        name = Path.GetFileName(name.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(input);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        token.ThrowIfCancellationRequested();
        if (extension == ".xlsx")
        {
            var workbook = SpreadsheetPreview.Read(payload, token);
            token.ThrowIfCancellationRequested();
            return new(name, null, null, workbook.Truncated) { Sheets = workbook.Sheets };
        }
        if (extension is ".png" or ".jpg" or ".jpeg" or ".pdf")
        {
            var bytes = File.ReadAllBytes(payload);
            try
            {
                token.ThrowIfCancellationRequested();
                // Complete cleanup before transferring ownership of the image buffer.
                workspace.Dispose();
                return extension == ".pdf" ? new(name, null, null, false) { Pdf = bytes } : new(name, null, bytes, false);
            }
            catch { Array.Clear(bytes); throw; }
        }
        string text = extension switch
        {
            ".docx" => ReadWord(payload, token),
            ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".htm" or ".log" or ".yaml" or ".yml" or ".ini" or ".cs" or ".sql" => ReadText(payload, token),
            _ => throw new NotSupportedException("此格式尚未接入预览。支持文本、PNG/JPEG、DOCX 正文、XLSX 和 PDF；其他格式请使用文件解密。")
        };
        token.ThrowIfCancellationRequested();
        return new(name, text[..Math.Min(text.Length, MaxCharacters)], null, text.Length > MaxCharacters);
    }

    private static string ReadText(string path, CancellationToken token)
    {
        string Read(Encoding encoding)
        {
            using var reader = new StreamReader(path, encoding, true);
            var buffer = new char[MaxCharacters + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                token.ThrowIfCancellationRequested();
                int read = reader.Read(buffer, count, Math.Min(4096, buffer.Length - count));
                if (read == 0) break;
                count += read;
            }
            return new string(buffer, 0, count);
        }
        string text;
        try { text = Read(new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Read(Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
        }
        if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t' and not '\f'))
            throw new NotSupportedException("文件含二进制控制字符，不能作为文本预览。");
        return text;
    }

    private static string ReadWord(string path, CancellationToken token)
    {
        using var file = File.OpenRead(path);
        ZipDirectoryPreflight.Validate(file, 10_000, token);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("DOCX 缺少正文。");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
        var text = new StringBuilder();
        const string word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        bool inText = false;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.NamespaceURI == word)
            {
                if (reader.LocalName == "t") inText = reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement;
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName is "p" or "tr") text.AppendLine();
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "tab" or "br" or "cr") text.Append(reader.LocalName == "tab" ? '\t' : '\n');
            }
            if (inText && reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace)
                text.Append(reader.Value.AsSpan(0, Math.Min(reader.Value.Length, MaxCharacters + 1 - text.Length)));
            if (text.Length > MaxCharacters) break;
        }
        return text.ToString();
    }
}
