using System.Text;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace PracticalTools.GPG;

internal sealed record ValidatedKey(PgpPublicKeyRing PublicRing, string SecretKey, string Passphrase);

internal static class KeyImportValidator
{
    public static ValidatedKey Parse(string armoredKey, string passphrase)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(armoredKey));
        using var decoder = PgpUtilities.GetDecoderStream(input);
        var factory = new PgpObjectFactory(decoder);
        var ring = factory.NextPgpObject();
        if (factory.NextPgpObject() != null) throw new ArgumentException("请每次导入一个完整的密钥环。");
        if (ring is PgpPublicKeyRing pub) return new(pub, "", "");
        if (ring is not PgpSecretKeyRing secretRing)
            throw new ArgumentException("请选择 ASCII armored GPG 公钥或私钥文件。");
        var keys = secretRing.GetSecretKeys().ToList();
        if (keys.Count == 0 || keys.Any(k => k.IsPrivateKeyEmpty))
            throw new ArgumentException("不支持缺少私钥材料的密钥环。");
        try
        {
            foreach (var key in keys)
                if (key.ExtractPrivateKey(passphrase.ToCharArray()) == null)
                    throw new ArgumentException("无法解锁私钥。");
        }
        catch (PgpException) { throw new ArgumentException("私钥口令不正确，或私钥已损坏。"); }
        using var publicBytes = new MemoryStream();
        foreach (var key in secretRing.GetPublicKeys()) key.Encode(publicBytes);
        return new(new PgpPublicKeyRing(publicBytes.ToArray()), GpgHelper.ExportPrivateKey(secretRing), passphrase);
    }
}
