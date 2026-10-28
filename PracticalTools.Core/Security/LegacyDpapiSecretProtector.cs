using PracticalTools.util;

namespace PracticalTools.Security;

/// <summary>Keep the original WPF format and legacy plaintext compatibility unchanged.</summary>
public sealed class LegacyDpapiSecretProtector : ISecretProtector
{
    public static LegacyDpapiSecretProtector Instance { get; } = new();
    private LegacyDpapiSecretProtector() { }
    public string Protect(string plaintext) => CryptoHelper.Encrypt(plaintext);
    public string Unprotect(string protectedText) => CryptoHelper.Decrypt(protectedText);
}
