using System.Text;

namespace PracticalTools.util;

public abstract class StringUtil
{
    /// <summary>
    /// 异或
    /// </summary>
    /// <param name="input"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static string Xor(string input, string key)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(key))
            return input; // 或抛异常

        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] result = new byte[inputBytes.Length];

        for (int i = 0; i < inputBytes.Length; i++)
        {
            // 循环使用 key（key 可以比 input 短）
            result[i] = (byte)(inputBytes[i] ^ keyBytes[i % keyBytes.Length]);
        }

        return Convert.ToBase64String(result); // 异或结果可能是非文本字节，用 Base64 编码返回
    }
    
    /// <summary>
    /// 反异或
    /// </summary>
    /// <param name="base64Input"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static string XorFromBase64(string base64Input, string key)
    {
        byte[] inputBytes = Convert.FromBase64String(base64Input);
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] result = new byte[inputBytes.Length];

        for (int i = 0; i < inputBytes.Length; i++)
        {
            result[i] = (byte)(inputBytes[i] ^ keyBytes[i % keyBytes.Length]);
        }

        return Encoding.UTF8.GetString(result);
    }
}