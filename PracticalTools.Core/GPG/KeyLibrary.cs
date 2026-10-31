using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Bcpg.OpenPgp;
using PracticalTools.Sqlite;

namespace PracticalTools.GPG;

/// <summary>密钥列表项摘要，不包含私钥和口令。</summary>
public sealed record KeySummary(long Id, string Name, string Fingerprint, bool HasSecret,
    DateTime? BeginsOn, DateTime? EndsOn, int KeySize, string Description)
{
    public string Kind => HasSecret ? "公私钥" : "仅公钥";
    public override string ToString() => $"{Name} · {Fingerprint[^16..]} · {Kind}";
}

/// <summary>解密或导出所需的私钥装甲文本及其保护口令。</summary>
public sealed record KeyMaterial(string Key, string Passphrase);

/// <summary>每次操作使用独立的 DI 作用域；列表和详情查询不会读出私钥或口令。</summary>
public sealed partial class KeyLibrary(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _writes = new(1, 1);

    /// <summary>在后台线程创建独立的 DI 作用域，并执行查询或写入操作。</summary>
    private Task<T> ReadAsync<T>(Func<MyDbContext, T> action) => Task.Run(() =>
    {
        using var scope = scopeFactory.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<MyDbContext>());
    });

    /// <summary>串行执行写操作，避免多个写入同时访问同一个 SQLite 数据库。</summary>
    private async Task<T> WriteAsync<T>(Func<MyDbContext, T> action)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try { return await ReadAsync(action).ConfigureAwait(false); }
        finally { _writes.Release(); }
    }

    /// <summary>返回按创建时间倒序排列的密钥摘要，不加载私钥内容。</summary>
    public Task<List<KeySummary>> ListAsync() => ReadAsync(db => db.GpgFile.AsNoTracking()
        .OrderByDescending(k => k.CreateTime)
        .Select(k => new { k.Id, k.IdentityName, k.PublicKey, HasSecret = k.SecretKey != "",
            k.ValidDurationBeginsOn, k.ValidDurationEndsOn, k.KeySize, k.Description })
        .ToList().Select(k => new KeySummary(k.Id, k.IdentityName, GpgHelper.GetFingerprint(k.PublicKey),
            k.HasSecret, AsUtc(k.ValidDurationBeginsOn), AsUtc(k.ValidDurationEndsOn), k.KeySize, k.Description ?? "")).ToList());

    /// <summary>SQLite 不保存时区；把取出的时间值显式标记为 UTC 后再返回界面。</summary>
    private static DateTime? AsUtc(DateTime? date) => date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc) : null;

    /// <summary>生成指定长度和有效期的 RSA 密钥对，并走导入流程写入密钥库。</summary>
    /// <param name="identity">用户身份（姓名或邮箱），会去除首尾空白。</param>
    /// <param name="passphrase">私钥保护口令。</param>
    /// <param name="bits">RSA 位数，允许 2048、3072 或 4096。</param>
    /// <param name="validDays">有效期天数，0 表示永不过期，上限 36500 天。</param>
    /// <returns>新密钥的摘要。</returns>
    /// <exception cref="ArgumentException">身份、口令、位数或有效期无效。</exception>
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

    /// <summary>
    /// 解析并导入 ASCII 装甲密钥。相同指纹已存在时，已有私钥或本次仅含公钥都会拒绝覆盖；
    /// 已有记录只有公钥而本次带有私钥时，则把私钥补充到原记录。
    /// </summary>
    /// <param name="armoredKey">ASCII 装甲格式的公钥或私钥。</param>
    /// <param name="passphrase">私钥保护口令；公钥可传空字符串。</param>
    /// <param name="name">显示名称，为空时取身份中的用户 ID，再退回到指纹。</param>
    /// <param name="description">备注。</param>
    /// <param name="imported">是否标记为用户导入；生成新密钥时传 false。</param>
    /// <returns>导入后的密钥摘要。</returns>
    /// <exception cref="InvalidOperationException">同指纹密钥已存在且不允许覆盖。</exception>
    public Task<KeySummary> ImportAsync(string armoredKey, string passphrase, string name, string description,
        bool imported = true) => WriteAsync(db =>
    {
        var parsed = KeyImportValidator.Parse(armoredKey, passphrase);
        var publicRing = parsed.PublicRing;
        var secret = parsed.SecretKey;
        passphrase = parsed.Passphrase;

        var primary = publicRing.GetPublicKey();
        var fingerprint = GpgHelper.GetFingerprint(publicRing);
        var publicKey = GpgHelper.ExportPublicKey(publicRing);
        // SQLite 无法直接按指纹查询，先取出 ID 和公钥，再在内存中计算指纹比对。
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

    /// <summary>读取指定密钥的公钥装甲文本。</summary>
    /// <param name="id">密钥记录 ID。</param>
    /// <returns>ASCII 装甲公钥。</returns>
    /// <exception cref="InvalidOperationException">记录不存在。</exception>
    public Task<string> GetPublicKeyAsync(long id) => ReadAsync(db => db.GpgFile.AsNoTracking()
        .Where(k => k.Id == id).Select(k => k.PublicKey).SingleOrDefault()
        ?? throw new InvalidOperationException("密钥已不存在，请刷新列表。"));

    /// <summary>读取指定密钥的私钥装甲文本和保护口令。</summary>
    /// <param name="id">密钥记录 ID。</param>
    /// <returns>私钥材料。</returns>
    /// <exception cref="InvalidOperationException">记录不存在，或该记录只有公钥。</exception>
    public Task<KeyMaterial> GetSecretAsync(long id) => ReadAsync(db =>
    {
        var key = db.GpgFile.AsNoTracking().SingleOrDefault(k => k.Id == id)
            ?? throw new InvalidOperationException("密钥已不存在，请刷新列表。");
        if (key.SecretKey.Length == 0) throw new InvalidOperationException("该记录只有公钥，无法解密或导出私钥。");
        return new KeyMaterial(key.SecretKey, key.Passphrase);
    });

    /// <summary>更新密钥名称和备注，并刷新更新时间。</summary>
    /// <param name="id">密钥记录 ID。</param>
    /// <param name="name">新名称，不能为空。</param>
    /// <param name="description">新备注。</param>
    /// <returns>记录存在且更新成功时为 true。</returns>
    /// <exception cref="ArgumentException">名称为空。</exception>
    public Task<bool> UpdateMetadataAsync(long id, string name, string description) => WriteAsync(db =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("名称不能为空。");
        var updatedAt = DateTime.Now;
        return db.GpgFile.Where(k => k.Id == id).ExecuteUpdate(set => set
            .SetProperty(k => k.IdentityName, name.Trim()).SetProperty(k => k.Description, description.Trim())
            .SetProperty(k => k.UpdateTime, (DateTime?)updatedAt)) == 1;
    });

    /// <summary>按 ID 删除密钥记录。</summary>
    /// <param name="id">密钥记录 ID。</param>
    /// <returns>记录存在且删除成功时为 true。</returns>
    public Task<bool> DeleteAsync(long id) => WriteAsync(db => db.GpgFile.Where(k => k.Id == id).ExecuteDelete() == 1);

    /// <summary>把公钥或私钥导出为 ASCII 装甲文件；先写临时文件再移动到目标路径，失败时清理临时文件。</summary>
    /// <param name="id">密钥记录 ID。</param>
    /// <param name="path">导出目标路径。</param>
    /// <param name="secret">true 导出私钥，false 导出公钥。</param>
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
