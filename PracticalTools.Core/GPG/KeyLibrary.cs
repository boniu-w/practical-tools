using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Bcpg.OpenPgp;
using PracticalTools.Sqlite;

namespace PracticalTools.GPG;

public sealed record KeySummary(long Id, string Name, string Fingerprint, bool HasSecret,
    DateTime? BeginsOn, DateTime? EndsOn, int KeySize, string Description)
{
    public string Kind => HasSecret ? "公私钥" : "仅公钥";
    public override string ToString() => $"{Name} · {Fingerprint[^16..]} · {Kind}";
}

public sealed record KeyMaterial(string Key, string Passphrase);

/// <summary>One scope per operation; lists/details never materialize passwords or private keys.</summary>
public sealed class KeyLibrary(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _writes = new(1, 1);

    private Task<T> ReadAsync<T>(Func<MyDbContext, T> action) => Task.Run(() =>
    {
        using var scope = scopeFactory.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<MyDbContext>());
    });

    private async Task<T> WriteAsync<T>(Func<MyDbContext, T> action)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try { return await ReadAsync(action).ConfigureAwait(false); }
        finally { _writes.Release(); }
    }

    public Task<List<KeySummary>> ListAsync() => ReadAsync(db => db.GpgFile.AsNoTracking()
        .OrderByDescending(k => k.CreateTime)
        .Select(k => new { k.Id, k.IdentityName, k.PublicKey, HasSecret = k.SecretKey != "",
            k.ValidDurationBeginsOn, k.ValidDurationEndsOn, k.KeySize, k.Description })
        .ToList().Select(k => new KeySummary(k.Id, k.IdentityName, GpgHelper.GetFingerprint(k.PublicKey),
            k.HasSecret, AsUtc(k.ValidDurationBeginsOn), AsUtc(k.ValidDurationEndsOn), k.KeySize, k.Description ?? "")).ToList());

    private static DateTime? AsUtc(DateTime? date) => date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc) : null;

    public async Task<KeySummary> GenerateAsync(string identity, string passphrase, int bits, int validDays)
    {
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("请输入身份和私钥保护口令。");
        if (bits is not (2048 or 3072 or 4096)) throw new ArgumentException("RSA 密钥长度须为 2048、3072 或 4096。");
        if (validDays < 0 || validDays > 36500) throw new ArgumentException("有效期须为 0–36500 天，0 表示永不过期。");
        var pair = await Task.Run(() => GpgHelper.GeneratePgpKeyPair(identity.Trim(), passphrase, bits,
            validDays == 0 ? null : (long)validDays * 86400)).ConfigureAwait(false);
        return await ImportAsync(GpgHelper.ExportPrivateKey(pair.secRing), passphrase, identity.Trim(), "", imported: false)
            .ConfigureAwait(false);
    }

    public Task<KeySummary> ImportAsync(string armoredKey, string passphrase, string name, string description,
        bool imported = true) => WriteAsync(db =>
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(armoredKey));
        using var decoder = PgpUtilities.GetDecoderStream(input);
        var factory = new PgpObjectFactory(decoder);
        var ring = factory.NextPgpObject();
        if (factory.NextPgpObject() != null) throw new ArgumentException("请每次导入一个完整的密钥环。");
        PgpPublicKeyRing publicRing;
        string secret = "";
        if (ring is PgpSecretKeyRing secretRing)
        {
            var keys = secretRing.GetSecretKeys().Cast<PgpSecretKey>().ToList();
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
            publicRing = new PgpPublicKeyRing(publicBytes.ToArray());
            secret = GpgHelper.ExportPrivateKey(secretRing);
        }
        else if (ring is PgpPublicKeyRing pub)
        {
            publicRing = pub;
            passphrase = "";
        }
        else throw new ArgumentException("请选择 ASCII armored GPG 公钥或私钥文件。");

        var primary = publicRing.GetPublicKey();
        var fingerprint = GpgHelper.GetFingerprint(publicRing);
        var publicKey = GpgHelper.ExportPublicKey(publicRing);
        var existingId = db.GpgFile.AsNoTracking().Select(k => new { k.Id, k.PublicKey }).ToList()
            .Where(k => GpgHelper.GetFingerprint(k.PublicKey) == fingerprint).Select(k => (long?)k.Id).SingleOrDefault();
        var existing = existingId.HasValue ? db.GpgFile.Find(existingId.Value) : null;
        if (existing != null && (existing.SecretKey.Length > 0 || secret.Length == 0))
            throw new InvalidOperationException("该指纹已在密钥库中；不会覆盖已有密钥。");
        var displayName = string.IsNullOrWhiteSpace(name)
            ? primary.GetUserIds().Cast<string>().FirstOrDefault() ?? fingerprint : name.Trim();
        var expires = primary.GetValidSeconds();
        var service = new GpgFileService(db);
        var entity = existing ?? service.GenerateGpgFile(displayName, passphrase);
        entity.IdentityName = displayName;
        entity.Identity = primary.GetUserIds().Cast<string>().FirstOrDefault() ?? displayName;
        entity.PublicKey = publicKey;
        entity.SecretKey = secret;
        entity.Passphrase = passphrase;
        entity.Description = description.Trim();
        entity.KeySize = primary.BitStrength;
        entity.ValidDurationBeginsOn = primary.CreationTime;
        entity.ValidDurationEndsOn = expires == 0 ? null : primary.CreationTime.AddSeconds(expires);
        entity.ValidDurationSeconds = expires;
        entity.IsImported = imported;
        entity.UpdateTime = DateTime.Now;
        if (existing == null) db.GpgFile.Add(entity);
        db.SaveChanges();
        return new KeySummary(entity.Id, displayName, fingerprint, secret.Length > 0,
            entity.ValidDurationBeginsOn, entity.ValidDurationEndsOn, entity.KeySize, entity.Description ?? "");
    });

    public Task<string> GetPublicKeyAsync(long id) => ReadAsync(db => db.GpgFile.AsNoTracking()
        .Where(k => k.Id == id).Select(k => k.PublicKey).SingleOrDefault()
        ?? throw new InvalidOperationException("密钥已不存在，请刷新列表。"));

    public Task<KeyMaterial> GetSecretAsync(long id) => ReadAsync(db =>
    {
        var key = db.GpgFile.AsNoTracking().SingleOrDefault(k => k.Id == id)
            ?? throw new InvalidOperationException("密钥已不存在，请刷新列表。");
        if (key.SecretKey.Length == 0) throw new InvalidOperationException("该记录只有公钥，无法解密或导出私钥。");
        return new KeyMaterial(key.SecretKey, key.Passphrase);
    });

    public Task<bool> UpdateMetadataAsync(long id, string name, string description) => WriteAsync(db =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("名称不能为空。");
        var updatedAt = DateTime.Now;
        return db.GpgFile.Where(k => k.Id == id).ExecuteUpdate(set => set
            .SetProperty(k => k.IdentityName, name.Trim()).SetProperty(k => k.Description, description.Trim())
            .SetProperty(k => k.UpdateTime, (DateTime?)updatedAt)) == 1;
    });

    public Task<bool> DeleteAsync(long id) => WriteAsync(db => db.GpgFile.Where(k => k.Id == id).ExecuteDelete() == 1);

    public async Task ExportAsync(long id, string path, bool secret)
    {
        var text = secret ? (await GetSecretAsync(id).ConfigureAwait(false)).Key
            : await GetPublicKeyAsync(id).ConfigureAwait(false);
        var target = Path.GetFullPath(path);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(text).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
