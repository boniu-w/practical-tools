namespace PracticalTools.GPG.Folders;

public sealed record FolderCryptoLimits(int MaxEntries = 100000, long MaxPlainBytes = 20L * 1024 * 1024 * 1024,
    long MaxZipBytes = 20L * 1024 * 1024 * 1024);
public sealed record FolderCryptoProgress(string Phase, string Item, int Completed, int Total, long Bytes, long TotalBytes);
public sealed record FolderCryptoResult(string OutputPath, int Files, int Directories, long PlainBytes);

public interface IFolderCryptoService
{
    Task<FolderCryptoResult> EncryptAsync(string source, string outputFile, string publicKey,
        IProgress<FolderCryptoProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<FolderCryptoResult> DecryptAsync(string inputFile, string outputDirectory, string secretKey, string passphrase,
        IProgress<FolderCryptoProgress>? progress = null, CancellationToken cancellationToken = default);
}
