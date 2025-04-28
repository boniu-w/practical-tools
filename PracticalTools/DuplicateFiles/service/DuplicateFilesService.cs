using System.IO;
using PracticalTools.DuplicateFiles.Model;
using PracticalTools.Sqlite;
using PracticalTools.util;

namespace PracticalTools.DuplicateFiles.service;

public class DuplicateFilesService
{
    public List<DuplicateFilesEntry>? DuplicateFilesList { get; set; }

    private readonly MyDbContext _myDbContext;

    public DuplicateFilesService(MyDbContext myDbContext)
    {
        _myDbContext = myDbContext;
    }

    public List<DuplicateFilesEntry>? List()
    {
        return _myDbContext.DuplicateFiles.ToList();
    }

    public int Add(DuplicateFilesEntry duplicateFilesEntry)
    {
        _myDbContext.Database.EnsureCreated(); // 确保数据库存在
        _myDbContext.DuplicateFiles.Add(duplicateFilesEntry);
        _myDbContext.SaveChanges();

        return 1;
    }

    public int Update(DuplicateFilesEntry duplicateFilesEntry)
    {
        _myDbContext.Database.EnsureCreated(); // 确保数据库存在
        _myDbContext.DuplicateFiles.Update(duplicateFilesEntry);
        _myDbContext.SaveChanges();
        return 1;
    }

    /// <summary>
    /// 扫描重复文件, 只扫描文件名, 不比较hash
    /// </summary>
    /// <param name="path">要扫描的路径</param>
    public List<string> ScanRepetitiveFile(string path)
    {
        return null;
    }

    /// <summary>
    /// 获取文件夹下的所有文件类型
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public List<string> GetAllFileExtensions(string path)
    {
        return FileUtil.GetAllFileExtensions(path);
    }

    public List<DuplicateFilesEntry> GetAllFile(string path)
    {
        List<DuplicateFilesEntry> entries = new List<DuplicateFilesEntry>();
        List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)> allFiles = FileUtil.GetAllFiles(path,
            new List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)>());
        if (allFiles?.Count > 0)
        {
            DuplicateFilesEntry entry = null;
            foreach ((string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed) file in allFiles)
            {
                entry = new DuplicateFilesEntry();
                entry.FileName = file.FileName;
                entry.FilePath = file.FilePath;
                entry.FileSize = MathUtil.ByteToKb(file.FileSize, 3);
                entry.FileCreateTime = file.createTime;
                entry.FileLastedModifiedTime = file.LastModifed;
                entry.ScanDirectory = path;
                entry.ScanTime = DateTime.Now;

                entries.Add(entry);
                // Console.WriteLine(entry.FileCreateTime);
            }
        }

        return entries;
    }

    /// <summary>
    /// 可停止
    /// </summary>
    /// <param name="path"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public List<DuplicateFilesEntry> GetAllFile(string path, CancellationToken token = default, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> entries = new List<DuplicateFilesEntry>();
        List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)> allFiles = FileUtil.GetAllFiles(path,
            new List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed)>(),
            token);
        if (allFiles?.Count > 0)
        {
            DuplicateFilesEntry entry = null;
            foreach ((string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed) file in allFiles)
            {
                entry = new DuplicateFilesEntry();
                entry.FileName = file.FileName;
                entry.FilePath = file.FilePath;
                entry.FileSize = MathUtil.ByteToKb(file.FileSize, 3);
                entry.FileCreateTime = file.createTime;
                entry.FileLastedModifiedTime = file.LastModifed;
                entry.ScanDirectory = path;
                entry.ScanTime = DateTime.Now;

                entries.Add(entry);
                // Console.WriteLine(entry.FileCreateTime);
            }
        }

        return entries;
    }

    public List<DuplicateFilesEntry> GetAllFiles(string path, CancellationToken token = default, string[]? allowedExtensions = null)
    {
        List<DuplicateFilesEntry> entries = new List<DuplicateFilesEntry>();
        List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed, string Md5)> allFiles = FileUtil.GetAllFiles(path,
            new List<(string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed, string Md5)>(),
            token,
            allowedExtensions);
        if (allFiles?.Count > 0)
        {
            DuplicateFilesEntry entry = null;
            foreach ((string FileName, string FilePath, long FileSize, DateTime createTime, DateTime LastModifed, string Md5) file in allFiles)
            {
                entry = new DuplicateFilesEntry();
                entry.FileName = file.FileName;
                entry.FilePath = file.FilePath;
                entry.FileSize = MathUtil.ByteToKb(file.FileSize, 3);
                entry.FileCreateTime = file.createTime;
                entry.FileLastedModifiedTime = file.LastModifed;
                entry.ScanDirectory = path;
                entry.ScanTime = DateTime.Now;
                entry.HashValue = file.Md5;

                entries.Add(entry);
                // Console.WriteLine(entry.FileCreateTime);
            }
        }

        return entries;
    }

    public void DeleteFiles(string[]? filePaths)
    {
        if (filePaths?.Length > 0)
        {
            foreach (string filePath in filePaths)
            {
                FileUtil.DeleteFile(filePath);
            }
        }
    }
}