using System.Text;

namespace PracticalTools.GPG.Folders;

internal sealed class ArchiveNames
{
    private readonly Dictionary<string, (string Raw, bool Directory)> _paths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _explicit = new(StringComparer.Ordinal);
    public int DirectoryCount => _paths.Values.Count(p => p.Directory);
    public string Add(string name, bool directory)
    {
        if (directory && name.EndsWith('/')) name = name[..^1];
        if (name.Length == 0 || name.Length > 1024 || name.StartsWith('/') || name.Contains('\\'))
            throw new InvalidDataException("归档路径不是支持的相对路径。");
        var parts = name.Split('/');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(char.IsControl) || part.IndexOfAny([':', '<', '>', '"', '|', '?', '*']) >= 0)
                throw new InvalidDataException($"归档中含不可移植或越界的路径：{name}");
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException($"归档包含保留名称：{name}");
        }
        for (var i = 1; i <= parts.Length; i++)
        {
            var prefix = string.Join('/', parts.Take(i));
            var key = prefix.Normalize(NormalizationForm.FormC).ToUpperInvariant();
            var isDirectory = i < parts.Length || directory;
            if (_paths.TryGetValue(key, out var existing) && (existing.Raw != prefix || existing.Directory != isDirectory))
                throw new InvalidDataException($"归档路径存在大小写、Unicode 或文件/目录冲突：{name}");
            _paths[key] = (prefix, isDirectory);
            if (i == parts.Length && !_explicit.Add(key)) throw new InvalidDataException($"归档路径重复：{name}");
        }
        return name;
    }
}
