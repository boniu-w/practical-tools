using PracticalTools.Constant;
using PracticalTools.Sqlite;
using PracticalTools.util;

namespace PracticalTools.GPG;

public class GpgFileService
{
    private readonly MyDbContext _myDbContext;

    public GpgFileService(MyDbContext myDbContext)
    {
        _myDbContext = myDbContext;
    }

    public List<GpgFile> List()
    {
        List<GpgFile> gpgFiles = _myDbContext.GpgFile.ToList();
        return gpgFiles;
    }

    public int SaveDemo()
    {
        string identity = "boniu-w@live.cn";
        string passphrase = "Soccer0boy$";
        string password = StringUtil.Xor(passphrase, CommonConst.Salt);
        int keySize = 2048;
        long validDuration = long.MaxValue;
        var (pubRing, secRing, beginsOn, endsOn) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, keySize);
        string pubKeyAscii = GpgHelper.ExportPublicKey(pubRing);
        string secKeyAscii = GpgHelper.ExportPrivateKey(secRing);

        GpgFile gpgFile = GenerateGpgFile(identity, password, secKeyAscii, pubKeyAscii, beginsOn, endsOn, "", keySize, validDuration);
        _myDbContext.GpgFile.Add(gpgFile);
        int saveChanges = _myDbContext.SaveChanges();
        return saveChanges;
    }

    public int Save(GpgFile gpgFile)
    {
        _myDbContext.GpgFile.Add(gpgFile);
        int saveChanges = _myDbContext.SaveChanges();
        return saveChanges;
    }

    public void Get()
    {
    }

    public void Delete()
    {
    }

    public void Update()
    {
    }

    public GpgFile GenerateGpgFile(
        string identity,
        string passphrase,
        string secretKey = "",
        string pubKey = "",
        DateTime? beginsOn = null,
        DateTime? endsOn = null,
        string description = "",
        int keySize = 2048,
        long validDuration = 0,
        string signaturePub = "",
        string signatureSec = "",
        string md5Pub = "",
        string md5Sec = ""
    )
    {
        GpgFile gpgFile = new GpgFile();
        long id = new SnowflakeId(1, 1).NextId();
        DateTimeOffset dateTimeOffset = DateTimeOffset.Now;
        long milliseconds = dateTimeOffset.ToUnixTimeMilliseconds();
        DateTime now = dateTimeOffset.DateTime;
        gpgFile.Id = id;
        gpgFile.Identity = identity;
        gpgFile.IdentityName = identity;
        gpgFile.Passphrase = passphrase;
        gpgFile.KeySize = keySize;
        gpgFile.SecretKey = secretKey;
        gpgFile.PublicKey = pubKey;
        gpgFile.ValidDurationBeginsOn = beginsOn;
        gpgFile.ValidDurationEndsOn = endsOn;
        gpgFile.ValidDurationSeconds = validDuration;
        gpgFile.Description = description;
        gpgFile.FileNamePublic = string.Concat("pub-", milliseconds, ".asc");
        gpgFile.FileNamePrivate = string.Concat("sec-", milliseconds, ".asc");
        gpgFile.Md5ValuePublic = md5Pub;
        gpgFile.Md5ValuePrivate = md5Sec;
        gpgFile.SignaturePublic = signaturePub;
        gpgFile.SignaturePrivate = signatureSec;
        gpgFile.FileCreateTime = now;
        gpgFile.Type = ".asc";
        gpgFile.CreateTime = now;
        gpgFile.UpdateTime = now;

        return gpgFile;
    }
}