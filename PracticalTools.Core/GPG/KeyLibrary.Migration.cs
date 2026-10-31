using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PracticalTools.Migration;

namespace PracticalTools.GPG;

public sealed partial class KeyLibrary
{
    /// <summary>
    /// 创建当前密钥库的一致性迁移快照：在同一事务内读取密钥、黑名单、查重扫描和文件记录。
    /// 返回前会校验数据结构与记录数量限制。
    /// </summary>
    /// <returns>可序列化为迁移包的数据快照。</returns>
    public Task<MigrationSnapshot> CreateMigrationSnapshotAsync() => ReadAsync(db =>
    {
        using var transaction = db.Database.BeginTransaction();
        var snapshot = new MigrationSnapshot
        {
            Keys = db.GpgFile.AsNoTracking().ToList(), Blacklist = db.Blacklist.AsNoTracking().ToList(),
            Scans = db.DuplicateFiles.AsNoTracking().ToList(), Files = db.MyFile.AsNoTracking().ToList()
        };
        transaction.Commit();
        snapshot.ValidateShape();
        return snapshot;
    });

    /// <summary>
    /// 校验迁移快照并解析其中的密钥，返回与快照 <c>Keys</c> 顺序一致的密钥列表。
    /// 同时校验私钥与公钥的指纹一致性、黑名单字段以及各类记录的 ID。
    /// </summary>
    /// <param name="snapshot">待校验的迁移快照。</param>
    /// <returns>解析后的密钥材料，索引与快照中的密钥一一对应。</returns>
    /// <exception cref="InvalidDataException">数据结构、密钥指纹或记录 ID 无效。</exception>
    /// <exception cref="ValidationException">黑名单记录缺少必填字段。</exception>
    internal static List<ValidatedKey> ValidateMigrationKeys(MigrationSnapshot snapshot)
    {
        snapshot.ValidateShape();
        var validated = new List<ValidatedKey>();
        foreach (var source in snapshot.Keys)
        {
            var parsed = KeyImportValidator.Parse(!string.IsNullOrEmpty(source.SecretKey) ? source.SecretKey : source.PublicKey,
                source.Passphrase ?? "");
            if (!string.IsNullOrEmpty(source.PublicKey))
            {
                var publicPart = KeyImportValidator.Parse(source.PublicKey, "");
                if (publicPart.SecretKey.Length != 0 || GpgHelper.GetFingerprint(publicPart.PublicRing) != GpgHelper.GetFingerprint(parsed.PublicRing))
                    throw new InvalidDataException("迁移包中的公钥与私钥指纹不匹配。");
                // 保留较新的公钥认证/子密钥信息，避免从私钥推导出更旧的公钥环。
                parsed = parsed with { PublicRing = publicPart.PublicRing };
            }
            validated.Add(parsed);
        }
        foreach (var record in snapshot.Blacklist) Validator.ValidateObject(record, new ValidationContext(record), true);
        if (snapshot.Blacklist.Any(r => r.Id <= 0) || snapshot.Scans.Any(r => string.IsNullOrWhiteSpace(r.Id)) ||
            snapshot.Files.Any(r => string.IsNullOrWhiteSpace(r.Id)))
            throw new InvalidDataException("迁移记录缺少有效 ID。");
        return validated;
    }

    /// <summary>
    /// 将迁移快照导入当前数据库：按指纹跳过已存在的密钥，按 ID 跳过重复记录，
    /// 其余数据在同一事务中写入。
    /// </summary>
    /// <param name="snapshot">已通过预览校验的迁移快照。</param>
    /// <returns>各类记录的新增数量和跳过数量。</returns>
    public Task<MigrationResult> ImportMigrationSnapshotAsync(MigrationSnapshot snapshot) => WriteAsync(db =>
    {
        var parsedKeys = ValidateMigrationKeys(snapshot);
        using var transaction = db.Database.BeginTransaction();
        // 已存在的密钥指纹，用于导入时跳过重复密钥。
        var knownFingerprints = db.GpgFile.Select(k => k.PublicKey).ToList().Select(GpgHelper.GetFingerprint).ToHashSet();
        var added = 0;
        var skipped = 0;
        var service = new GpgFileService(db);
        for (var i = 0; i < snapshot.Keys.Count; i++)
        {
            var source = snapshot.Keys[i];
            var parsed = parsedKeys[i];
            if (!knownFingerprints.Add(GpgHelper.GetFingerprint(parsed.PublicRing))) { skipped++; continue; }
            var entity = service.GenerateGpgFile(source.Identity ?? "", parsed.Passphrase);
            // SetValues 会用快照中的旧 ID 覆盖数据库生成的新 ID，先暂存再恢复，避免主键冲突。
            var freshId = entity.Id;
            db.Entry(entity).CurrentValues.SetValues(source);
            entity.Id = freshId;
            entity.IdentityName = string.IsNullOrWhiteSpace(source.IdentityName) ? source.Identity ?? "" : source.IdentityName;
            entity.PublicKey = GpgHelper.ExportPublicKey(parsed.PublicRing);
            entity.SecretKey = parsed.SecretKey;
            entity.Passphrase = parsed.Passphrase;
            var primary = parsed.PublicRing.GetPublicKey();
            entity.KeySize = primary.BitStrength;
            entity.ValidDurationBeginsOn = primary.CreationTime;
            entity.ValidDurationSeconds = primary.GetValidSeconds();
            entity.ValidDurationEndsOn = entity.ValidDurationSeconds == 0 ? null : primary.CreationTime.AddSeconds(entity.ValidDurationSeconds);
            db.GpgFile.Add(entity);
            added++;
        }

        // 按记录 ID 去重，只导入当前库中不存在的记录。
        var blacklistIds = db.Blacklist.Select(r => r.Id).ToHashSet();
        var scanIds = db.DuplicateFiles.Select(r => r.Id).ToHashSet();
        var fileIds = db.MyFile.Select(r => r.Id).ToHashSet();
        var blacklist = snapshot.Blacklist.Where(r => blacklistIds.Add(r.Id)).ToList();
        var scans = snapshot.Scans.Where(r => scanIds.Add(r.Id)).ToList();
        var files = snapshot.Files.Where(r => fileIds.Add(r.Id)).ToList();
        // 跳过的数量 = 重复密钥 + 各类已存在的重复记录。
        skipped += snapshot.Blacklist.Count - blacklist.Count + snapshot.Scans.Count - scans.Count + snapshot.Files.Count - files.Count;
        db.Blacklist.AddRange(blacklist);
        db.DuplicateFiles.AddRange(scans);
        db.MyFile.AddRange(files);
        db.SaveChanges();
        transaction.Commit();
        return new MigrationResult(added, blacklist.Count, scans.Count, files.Count, skipped);
    });
}
