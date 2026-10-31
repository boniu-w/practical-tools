using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PracticalTools.Storage;

namespace PracticalTools.FileManager;

/// <summary>待整理的单个文件及其执行阶段用于复核的大小、时间戳和哈希。</summary>
public sealed record OrganizeItem(string Source, string Target, long Length, DateTime Created, DateTime Modified, string Hash);

/// <summary>预览阶段生成的整理计划，包含整理根目录和逐文件条目。</summary>
public sealed record OrganizePlan(string Root, IReadOnlyList<OrganizeItem> Items);

/// <summary>整理结果：实际移动数量、逐文件错误以及是否因取消而中止。</summary>
public sealed record OrganizeResult(int Moved, IReadOnlyList<string> Errors, bool Cancelled);

/// <summary>按扩展名把目录中的文件归入同名子目录；先预览生成计划，再执行移动。</summary>
public sealed class FileOrganizer
{
    /// <summary>
    /// 扫描目录的直接子文件，生成按扩展名分类的整理计划。
    /// 预览时会记录每个文件的大小、时间戳并计算 SHA-256，供执行阶段复核。
    /// </summary>
    /// <param name="directory">要整理的目录，必须是已存在的目录。</param>
    /// <param name="extensions">以逗号、分号或空白分隔的扩展名列表，可带前导点。</param>
    /// <param name="token">取消标记；取消时抛出 <see cref="OperationCanceledException"/>。</param>
    /// <returns>包含源路径、目标路径、大小、时间戳和哈希的整理计划。</returns>
    /// <exception cref="DirectoryNotFoundException">目标目录不存在。</exception>
    /// <exception cref="ArgumentException">扩展名列表为空或包含非法字符。</exception>
    /// <exception cref="InvalidDataException">待整理文件超过 10000 个。</exception>
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
            // 目标目录按扩展名命名，文件名保持不变。
            var target = Path.Combine(root, extension, info.Name);
            if (Directory.Exists(Path.GetDirectoryName(target))) LocalFileAccess.NoLinks(Path.GetDirectoryName(target)!);
            using var stream = LocalFileAccess.Open(path);
            if (!stream.CanSeek) throw new IOException("只支持普通文件。");
            var hash = Hash(stream, token);
            // 读取期间文件若被修改或替换，快照校验会让预览失败。
            LocalFileAccess.CheckSnapshot(path, root, length, created, modified);
            items.Add(new(path, target, length, created, modified, hash));
        }
        return new OrganizePlan(root, items.AsReadOnly());
    }, token);

    /// <summary>
    /// 执行预览生成的计划：逐项复核快照与哈希后，把文件移动到扩展名对应的子目录。
    /// 单个文件失败会记入错误并继续处理；遇到取消则停止并返回已移动数量。
    /// </summary>
    /// <param name="plan">由 <see cref="PreviewAsync"/> 生成的整理计划。</param>
    /// <param name="token">取消标记；取消后不再处理剩余文件。</param>
    /// <returns>移动数量、错误列表以及是否因取消而中止。</returns>
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
                // 打开文件后、移动前再次核对，尽量缩小文件被替换的竞态窗口。
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
    /// <summary>分块计算流的 SHA-256；每读取 64 KB 检查一次取消请求。</summary>
    /// <param name="stream">要计算哈希的流，从当前位置开始读取。</param>
    /// <param name="token">取消标记。</param>
    /// <returns>大写十六进制 SHA-256 字符串。</returns>
    private static string Hash(Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; int read;
        while ((read = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
