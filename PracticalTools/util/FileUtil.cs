using System.IO;
using System.Security.Cryptography;

namespace PracticalTools.util;

public class FileUtil
{
    /// <summary>
    /// md5 较快
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static byte[] ComputeFileHashMD5(string filePath)
    {
        // 使用 MD5 算法
        MD5 md5 = MD5.Create();
        return Md5Hash(filePath, md5);
    }

    public static byte[] Md5Hash(string filePath, MD5 md5)
    {
        // 自动释放文件流资源
        using FileStream stream = File.OpenRead(filePath);

        // 动态计算缓冲区大小（保持与 Java 相同逻辑）
        int bufferSize = CalculateBufferSize(new FileInfo(filePath).Length);
        byte[] buffer = new byte[bufferSize];

        int bytesRead;
        // 分块读取文件更新哈希
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        // 完成最终哈希计算
        md5.TransformFinalBlock(buffer, 0, 0);
        return md5.Hash!;
    }

    /// <summary>
    /// sha256 较慢
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static byte[] ComputeFileHashSHA256(string filePath)
    {
        // 使用 SHA256 算法
        using SHA256 sha256 = SHA256.Create();
        return Sha256Hash();

        byte[] Sha256Hash()
        {
            // 自动释放文件流资源
            using FileStream stream = File.OpenRead(filePath);

            // 动态计算缓冲区大小（保持与 Java 相同逻辑）
            int bufferSize = CalculateBufferSize(new FileInfo(filePath).Length);
            byte[] buffer = new byte[bufferSize];

            int bytesRead;
            // 分块读取文件更新哈希
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            // 完成最终哈希计算
            sha256.TransformFinalBlock(buffer, 0, 0);
            return sha256.Hash!;
        }
    }

    private static int CalculateBufferSize(long fileLength)
    {
        // 缓冲区大小逻辑（512B - 64KB）
        return (int)Math.Clamp(fileLength, 512, 65536);
    }
}