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

    /// <summary>Deletes only directories this class could have created under <paramref name="parent"/>.</summary>
    internal static int CleanupStale(string parent)
    {
        parent = Path.GetFullPath(parent);
        if (!Directory.Exists(parent)) return 0;
        string[] directories;
        try { directories = Directory.GetDirectories(parent, Prefix + "*", SearchOption.TopDirectoryOnly); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }

        int removed = 0;
        foreach (string directory in directories)
        {
            if (!IsOwnedName(Path.GetFileName(directory))) continue;
            try
            {
                LocalFileAccess.NoLinks(directory);
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep going: one locked leftover must not block the rest.
            }
        }
        return removed;
    }

    private static bool IsOwnedName(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(Prefix.Length), "N", out _);

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
