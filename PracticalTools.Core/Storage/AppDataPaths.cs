namespace PracticalTools.Storage;

/// <summary>Avalonia data is deliberately independent of the legacy WPF installation directory.</summary>
public sealed class AppDataPaths
{
    public string Root { get; }
    public string DatabasePath => Path.Combine(Root, "sqlite-PracticalTools.db");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string InstanceLockPath => Path.Combine(Root, "app.lock");

    public AppDataPaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PracticalTools", "Avalonia"));
    }

    public void EnsureDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LogsDirectory);
        }
        else
        {
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(Root, ownerOnly);
            Directory.CreateDirectory(LogsDirectory, ownerOnly);
        }
    }
}
