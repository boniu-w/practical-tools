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
        int keySize = 2048;
        long validDuration = long.MaxValue;
        var (pubRing, secRing, beginsOn, endOn) = GpgHelper.GeneratePgpKeyPair(identity, passphrase, keySize);
        string pubKeyAscii = GpgHelper.ExportPublicKey(pubRing);
        string secKeyAscii = GpgHelper.ExportPrivateKey(secRing);

        GpgFile gpgFile = GenerateGpgFile(identity, passphrase, keySize, validDuration, pubKeyAscii, secKeyAscii);
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

    private GpgFile GenerateGpgFile(
        string identity,
        string passphrase,
        int keySize,
        long validDuration = 0,
        string pubKey = "",
        string secretKey = "",
        string signaturePub = "",
        string signatureSec = "",
        string md5Pub = "",
        string md5Sec = "",
        string description = ""
    )
    {
        GpgFile gpgFile = new GpgFile();
        long id = new SnowflakeId(1, 1).NextId();
        DateTimeOffset dateTimeOffset = DateTimeOffset.Now;
        long milliseconds = dateTimeOffset.ToUnixTimeMilliseconds();
        DateTime now = dateTimeOffset.DateTime;
        gpgFile.CreateTime = now;
        gpgFile.UpdateTime = now;
        gpgFile.Type = ".asc";
        gpgFile.Id = id;
        gpgFile.SignaturePublic = signaturePub;
        gpgFile.SignaturePrivate = signatureSec;
        gpgFile.FileNamePublic = string.Concat("pub-", milliseconds, ".asc");
        gpgFile.FileNamePrivate = string.Concat("sec-", milliseconds, ".asc");
        gpgFile.Md5ValuePublic = md5Pub;
        gpgFile.Md5ValuePrivate = md5Sec;
        gpgFile.FileCreateTime = now;
        gpgFile.ValidDurationSeconds = validDuration;
        gpgFile.SecretKey = secretKey;
        gpgFile.PublicKey = pubKey;
        gpgFile.Identity = identity;
        gpgFile.Passphrase = passphrase;
        gpgFile.KeySize = keySize;
        gpgFile.Description = description;

        return gpgFile;
    }
}