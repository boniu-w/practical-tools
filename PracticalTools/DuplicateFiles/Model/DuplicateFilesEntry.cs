namespace PracticalTools.DuplicateFiles.Model;

public class DuplicateFilesEntry
{
    public string Id { get; set; }
    public string FileName { get; set; }
    public string? FilePath { get; set; }
    public DateTime? FileCreateTime { get; set; }
    public double? FileSize { get; set; }
    public DateTime? FileLastedModifiedTime { get; set; }
    public string? ScanDirectory { get; set; }
    public DateTime? ScanTime { get; set; }
    public string? HashValue { get; set; }
}