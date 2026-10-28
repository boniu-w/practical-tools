using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PracticalTools.Migration;

namespace PracticalTools.GPG;

public sealed partial class KeyLibrary
{
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
                // Preserve newer public certifications/subkeys instead of deriving an older public ring from the secret.
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

    public Task<MigrationResult> ImportMigrationSnapshotAsync(MigrationSnapshot snapshot) => WriteAsync(db =>
    {
        var parsedKeys = ValidateMigrationKeys(snapshot);
        using var transaction = db.Database.BeginTransaction();
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

        var blacklistIds = db.Blacklist.Select(r => r.Id).ToHashSet();
        var scanIds = db.DuplicateFiles.Select(r => r.Id).ToHashSet();
        var fileIds = db.MyFile.Select(r => r.Id).ToHashSet();
        var blacklist = snapshot.Blacklist.Where(r => blacklistIds.Add(r.Id)).ToList();
        var scans = snapshot.Scans.Where(r => scanIds.Add(r.Id)).ToList();
        var files = snapshot.Files.Where(r => fileIds.Add(r.Id)).ToList();
        skipped += snapshot.Blacklist.Count - blacklist.Count + snapshot.Scans.Count - scans.Count + snapshot.Files.Count - files.Count;
        db.Blacklist.AddRange(blacklist);
        db.DuplicateFiles.AddRange(scans);
        db.MyFile.AddRange(files);
        db.SaveChanges();
        transaction.Commit();
        return new MigrationResult(added, blacklist.Count, scans.Count, files.Count, skipped);
    });
}
