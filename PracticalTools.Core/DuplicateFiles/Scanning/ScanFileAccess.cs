using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PracticalTools.DuplicateFiles.Scanning;

internal static class ScanFileAccess
{
    // Conservative path identity avoids treating case/Unicode aliases as two independent copies.
    // On case-sensitive volumes this may omit case-only/normalization-only distinct paths.
    internal static string Identity(string path) => Path.GetFullPath(path).Normalize(NormalizationForm.FormC).ToUpperInvariant();

    internal static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("路径包含符号链接或重解析点，请选择实际目录。");
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
        }
    }

    internal static FileStream Open(string path, bool allowDelete = false)
    {
        if (OperatingSystem.IsWindows())
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                allowDelete ? FileShare.Read | FileShare.Delete : FileShare.Read, 65536, FileOptions.SequentialScan);
        // Nonblocking/no-follow prevents a FIFO or a link replacement from blocking the scanner at open().
        var flags = OperatingSystem.IsMacOS() ? 0x0004 | 0x0100
            : OperatingSystem.IsLinux() ? 0x0800 | 0x20000
            : throw new PlatformNotSupportedException("扫描目前支持 Windows、macOS 和 Linux。");
        var descriptor = OpenUnix(path, flags);
        if (descriptor < 0) throw new IOException($"无法打开文件（系统错误 {Marshal.GetLastPInvokeError()}）。");
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try { return new FileStream(handle, FileAccess.Read, 65536); }
        catch { handle.Dispose(); throw; }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnix([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    internal static void CheckSnapshot(ScannedFile file)
    {
        var relative = Path.GetRelativePath(file.Root, file.FilePath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("文件不在扫描目录内。");
        NoLinks(file.FilePath);
        var info = new FileInfo(file.FilePath);
        if (info.Length != file.Length || info.LastWriteTimeUtc != file.LastWriteUtc || info.CreationTimeUtc != file.CreationUtc)
            throw new IOException("文件在扫描后发生变化，请重新扫描。");
    }
}
