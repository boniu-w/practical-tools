using System.Diagnostics;

namespace PracticalTools.GPG;

public class GpgHelper
{
    
    private const string GpgPath = @"C:\Program Files (x86)\gnupg\bin\gpg.exe";

    // --batch：非交互模式，遇到问题报错，不弹窗
    // --yes：自动回答“yes”给信任提示等问题
    public static void EncryptFile(string inputFile, string outputFile, string recipient)
    {
        var args = $"--batch --yes --output \"{outputFile}\" --encrypt --recipient \"{recipient}\" \"{inputFile}\"";
        RunGpgCommand(args);
    }

    public static void DecryptFile(string inputFile, string outputFile)
    {
        var args = $"--output \"{outputFile}\" --decrypt \"{inputFile}\"";
        RunGpgCommand(args);
    }

    private static void RunGpgCommand(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GpgPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"GPG error: {error}");
        }
    }
}