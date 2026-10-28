using System.Text.Json.Serialization;
using PracticalTools.Blacklist.Model;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.FileManager.Model;
using PracticalTools.GPG;

namespace PracticalTools.Migration;

public sealed class MigrationSnapshot
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonRequired] public List<GpgFile> Keys { get; set; } = [];
    [JsonRequired] public List<BlacklistEntry> Blacklist { get; set; } = [];
    [JsonRequired] public List<DuplicateFilesEntry> Scans { get; set; } = [];
    [JsonRequired] public List<MyFile> Files { get; set; } = [];

    public void ValidateShape()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("不支持此迁移包的数据版本。");
        if (Keys == null || Blacklist == null || Scans == null || Files == null ||
            Keys.Count > 10000 || (long)Blacklist.Count + Scans.Count + Files.Count > 100000 ||
            Keys.Any(k => k == null) || Blacklist.Any(k => k == null) || Scans.Any(k => k == null) || Files.Any(k => k == null))
            throw new InvalidDataException("迁移包的数据结构或记录数量无效。");
    }
}

public sealed record MigrationPreview(string Digest, int Keys, int Blacklist, int Scans, int Files, string[] Fingerprints);
public sealed record MigrationResult(int KeysAdded, int BlacklistAdded, int ScansAdded, int FilesAdded, int Skipped)
{
    public override string ToString() => $"已导入密钥 {KeysAdded}、黑名单 {BlacklistAdded}、扫描记录 {ScansAdded}、文件记录 {FilesAdded}；跳过重复项 {Skipped}。";
}
