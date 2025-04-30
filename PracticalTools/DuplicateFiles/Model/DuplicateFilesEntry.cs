using PracticalTools.util;

namespace PracticalTools.DuplicateFiles.Model;

public class DuplicateFilesEntry
{
    public string Id { get; set; }
    [ColumnHeader("文件名")] public string FileName { get; set; }
    [ColumnHeader("文件路径")] public string? FilePath { get; set; }
    public DateTime? FileCreateTime { get; set; }
    [ColumnHeader("文件大小[K]")]  public double? FileSize { get; set; }
    public DateTime? FileLastedModifiedTime { get; set; }
    public string? ScanDirectory { get; set; }
    public DateTime? ScanTime { get; set; }
    [ColumnHeader("Hash值[MD5]")] public string? Md5Value { get; set; }

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
               $"HashValue: {Md5Value}";
    }
}