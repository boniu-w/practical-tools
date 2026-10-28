using Microsoft.VisualBasic.FileIO;

namespace PracticalTools.DuplicateFiles.Scanning;

public sealed class PlatformFileRemoval : IFileRemoval
{
    public bool CanRecycle => OperatingSystem.IsWindows();

    public void Remove(string path, FileRemovalMode mode)
    {
        if (mode == FileRemovalMode.Permanent) { File.Delete(path); return; }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("当前平台尚未接入系统回收站，请明确选择永久删除。");
        // Let Windows show its native confirmations/errors. Never catch an error and fall back to File.Delete.
        FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    }
}
