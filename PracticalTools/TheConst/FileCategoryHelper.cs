using System.IO;

namespace PracticalTools.TheConst;

public class FileCategoryHelper
{
    private static readonly Dictionary<string, string> ExtensionCategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 音频
        [".mp3"] = "Audio", [".wav"] = "Audio", [".flac"] = "Audio", [".aac"] = "Audio", [".ogg"] = "Audio",
        [".wma"] = "Audio", [".m4a"] = "Audio", [".aiff"] = "Audio", [".alac"] = "Audio",

        // 视频
        [".mp4"] = "Video", [".avi"] = "Video", [".mkv"] = "Video", [".mov"] = "Video", [".flv"] = "Video",
        [".wmv"] = "Video", [".webm"] = "Video", [".mpeg"] = "Video", [".3gp"] = "Video",

        // 文档
        [".txt"] = "Document", [".doc"] = "Document", [".docx"] = "Document", [".pdf"] = "Document",
        [".odt"] = "Document", [".rtf"] = "Document", [".md"] = "Document",

        // 表格
        [".xls"] = "Spreadsheet", [".xlsx"] = "Spreadsheet", [".ods"] = "Spreadsheet", [".csv"] = "Spreadsheet",

        // 演示文稿
        [".ppt"] = "Presentation", [".pptx"] = "Presentation", [".odp"] = "Presentation",

        // 图片
        [".jpg"] = "Image", [".jpeg"] = "Image", [".png"] = "Image", [".bmp"] = "Image",
        [".gif"] = "Image", [".tiff"] = "Image", [".svg"] = "Image", [".webp"] = "Image", [".heic"] = "Image",

        // 压缩包
        [".zip"] = "Archive", [".rar"] = "Archive", [".7z"] = "Archive", [".tar"] = "Archive",
        [".gz"] = "Archive", [".bz2"] = "Archive", [".xz"] = "Archive", [".iso"] = "Archive",

        // 可执行
        [".exe"] = "Executable", [".msi"] = "Executable", [".bat"] = "Executable", [".app"] = "Executable", [".apk"] = "Executable",

        // 脚本
        [".ps1"] = "Script", [".sh"] = "Script", [".py"] = "Script", [".js"] = "Script", [".ts"] = "Script",
        [".rb"] = "Script", [".php"] = "Script", [".lua"] = "Script", [".pl"] = "Script",

        // 字体
        [".ttf"] = "Font", [".otf"] = "Font", [".woff"] = "Font", [".woff2"] = "Font",
    };

    public static string GetCategory(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLower();
        return ext != null && ExtensionCategoryMap.TryGetValue(ext, out var category) ? category : "Unknown";
    }

    public static bool IsAudio(string filePath) => GetCategory(filePath) == "Audio";
    public static bool IsVideo(string filePath) => GetCategory(filePath) == "Video";
    public static bool IsDocument(string filePath) => GetCategory(filePath) == "Document";
    public static bool IsImage(string filePath) => GetCategory(filePath) == "Image";
    public static bool IsArchive(string filePath) => GetCategory(filePath) == "Archive";
    public static bool IsExecutable(string filePath) => GetCategory(filePath) == "Executable";
    public static bool IsScript(string filePath) => GetCategory(filePath) == "Script";
    public static bool IsFont(string filePath) => GetCategory(filePath) == "Font";
    public static bool IsSpreadsheet(string filePath) => GetCategory(filePath) == "Spreadsheet";
    public static bool IsPresentation(string filePath) => GetCategory(filePath) == "Presentation";
}