using System.Security.AccessControl;
using System.Security.Principal;
using PracticalTools.Storage;

namespace PracticalTools.GPG.Folders;

internal sealed class PrivateWorkspace : IDisposable
{
    private const string Prefix = ".practical-tools-folder-";
    private readonly string _parent;
    public string Root { get; }
    public PrivateWorkspace(string parent)
    {
        _parent = Path.GetFullPath(parent);
        LocalFileAccess.NoLinks(_parent);
        Root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new IOException("无法确定当前用户。");
            var security = new DirectorySecurity();
            security.SetOwner(owner);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.CreateDirectory(Root);
        }
        else Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static FileStream CreateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    public void Dispose()
    {
        // Recursive cleanup is restricted to the exact workspace allocated by this instance.
        if (Path.GetDirectoryName(Path.GetFullPath(Root)) != _parent || !Path.GetFileName(Root).StartsWith(Prefix, StringComparison.Ordinal))
            throw new IOException("拒绝清理非工作目录。");
        if (!Directory.Exists(Root)) return;
        LocalFileAccess.NoLinks(Root);
        Directory.Delete(Root, recursive: true);
    }
}
