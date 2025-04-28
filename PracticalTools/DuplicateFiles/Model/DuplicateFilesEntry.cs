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

    // public override string ToString()
    // {
    //     return
    //         $"Id: {Id}, FileName: {FileName}, FilePath: {FilePath}, FileCreateTime: {FileCreateTime}, FileSize: {FileSize}, FileLastedModifiedTime: {FileLastedModifiedTime}, ScanDirectory: {ScanDirectory}, ScanTime: {ScanTime}, HashValue: {HashValue}";
    // }
    
    public override string ToString()
    {
        return $"Id: {Id}\n" +
               $"FileName: {FileName}\n" +
               $"FilePath: {FilePath}\n" +
               $"FileCreateTime: {FileCreateTime}\n" +
               $"FileSize: {FileSize}\n" +
               $"FileLastedModifiedTime: {FileLastedModifiedTime}\n" +
               $"ScanDirectory: {ScanDirectory}\n" +
               $"ScanTime: {ScanTime}\n" +
               $"HashValue: {HashValue}";
    }
}