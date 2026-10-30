using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PracticalTools.Storage;

namespace PracticalTools.FileManager;

public sealed record OrganizeItem(string Source, string Target, long Length, DateTime Created, DateTime Modified, string Hash);
public sealed record OrganizePlan(string Root, IReadOnlyList<OrganizeItem> Items);
public sealed record OrganizeResult(int Moved, IReadOnlyList<string> Errors, bool Cancelled);

public sealed class FileOrganizer
{
    public Task<OrganizePlan> PreviewAsync(string directory, string extensions, CancellationToken token = default) => Task.Run(() =>
    {
        var root = Path.GetFullPath(directory); LocalFileAccess.NoLinks(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("请选择现有目录。");
        var suffixes = extensions.Split([',', '，', ';', '；', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('.').ToLowerInvariant()).Distinct().ToHashSet();
        if (suffixes.Count == 0 || suffixes.Any(s => !Regex.IsMatch(s, "^[a-z0-9_-]{1,20}$")))
            throw new ArgumentException("请输入字母、数字、下划线或短横线组成的后缀，以逗号分隔。");
        var items = new List<OrganizeItem>();
        foreach (var path in Directory.EnumerateFiles(root))
        {
            token.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (!suffixes.Contains(extension)) continue;
            if (items.Count >= 10000) throw new InvalidDataException("单次最多整理 10000 个文件。");
            LocalFileAccess.NoLinks(path);
            var info = new FileInfo(path);
            long length = info.Length; var created = info.CreationTimeUtc; var modified = info.LastWriteTimeUtc;
            var target = Path.Combine(root, extension, info.Name);
            if (Directory.Exists(Path.GetDirectoryName(target))) LocalFileAccess.NoLinks(Path.GetDirectoryName(target)!);
            using var stream = LocalFileAccess.Open(path);
            if (!stream.CanSeek) throw new IOException("只支持普通文件。");
            var hash = Hash(stream, token);
            LocalFileAccess.CheckSnapshot(path, root, length, created, modified);
            items.Add(new(path, target, length, created, modified, hash));
        }
        return new OrganizePlan(root, items.AsReadOnly());
    }, token);

    public Task<OrganizeResult> ApplyAsync(OrganizePlan plan, CancellationToken token = default) => Task.Run(() =>
    {
        int moved = 0; var errors = new List<string>();
        foreach (var item in plan.Items)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                if (Path.GetDirectoryName(item.Source) != plan.Root) throw new IOException("源文件必须是选定目录的直接子文件。");
                string extension = Path.GetExtension(item.Source).TrimStart('.').ToLowerInvariant();
                if (!Regex.IsMatch(extension, "^[a-z0-9_-]{1,20}$") ||
                    item.Target != Path.Combine(plan.Root, extension, Path.GetFileName(item.Source))) throw new IOException("整理目标无效。");
                LocalFileAccess.CheckSnapshot(item.Source, plan.Root, item.Length, item.Created, item.Modified);
                using var stream = LocalFileAccess.Open(item.Source, allowDelete: true);
                if (Hash(stream, token) != item.Hash) throw new IOException("文件内容已变化，请重新预览。");
                var parent = Path.GetDirectoryName(item.Target)!;
                if (Directory.Exists(parent)) LocalFileAccess.NoLinks(parent);
                else Directory.CreateDirectory(parent);
                LocalFileAccess.NoLinks(parent);
                LocalFileAccess.CheckSnapshot(item.Source, plan.Root, item.Length, item.Created, item.Modified);
                if (token.IsCancellationRequested) break;
                File.Move(item.Source, item.Target, overwrite: false); moved++;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { errors.Add($"{item.Source}：{ex.Message}"); }
        }
        return new OrganizeResult(moved, errors, token.IsCancellationRequested);
    });
    private static string Hash(Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; int read;
        while ((read = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
