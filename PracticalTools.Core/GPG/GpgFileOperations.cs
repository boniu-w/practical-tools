namespace PracticalTools.GPG;

/// <summary>Only publish output after successful encryption or integrity-checked decryption.</summary>
public static class GpgFileOperations
{
    public static Task EncryptAsync(string input, string output, string publicKey) =>
        WriteAsync(input, output, temporary => GpgHelper.EncryptFileByPublicString(input, temporary, publicKey));

    public static Task DecryptAsync(string input, string output, string secretKey, string passphrase) =>
        WriteAsync(input, output, temporary =>
            GpgHelper.DecryptFileByPublicString(input, temporary, secretKey, passphrase));

    private static async Task WriteAsync(string input, string output, Func<string, Task> operation)
    {
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);
        if (!File.Exists(input)) throw new FileNotFoundException("请选择存在的输入文件。", input);
        if (string.Equals(input, output, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("输入和输出不能是同一个文件。");
        if (File.Exists(output)) throw new IOException("输出文件已存在，请选择其他文件名。");

        var temporary = Path.Combine(Path.GetDirectoryName(output)!, $".practical-tools-{Guid.NewGuid():N}.tmp");
        try
        {
            // Decrypted temporary files must be private from the moment they are created.
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (new FileStream(temporary, options)) { }
            await operation(temporary).ConfigureAwait(false);
            File.Move(temporary, output, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
