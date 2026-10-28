namespace PracticalTools.Storage;

/// <summary>Keep the file handle open for the process lifetime; never unlink the lock file.</summary>
public sealed class SingleInstanceLease : IDisposable
{
    private readonly FileStream _handle;
    private SingleInstanceLease(FileStream handle) => _handle = handle;
    public static SingleInstanceLease Acquire(string path) =>
        new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
    public void Dispose() => _handle.Dispose();
}
