namespace PracticalTools.DuplicateFiles.Model;

public class DuplicateFilesEntry
{
    public string Id { get; set; }
    public string FileName { get; set; }
    public string? FilePath { get; set; }
    public DateTime? FileCreateTime { get; set; }
    public double? FileSize { get; set; }
    
    
}