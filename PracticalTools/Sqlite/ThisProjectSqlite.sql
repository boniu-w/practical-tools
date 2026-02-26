CREATE TABLE
    IF
    NOT EXISTS DuplicateFiles ( Id TEXT PRIMARY KEY, 
                                FileName TEXT NOT NULL,
                                FilePath TEXT,
                                FileSize Real,
                                FileCreateTime TEXT );
-- Gpg密钥表
CREATE TABLE 
    IF NOT EXISTS "GpgFile" (
                           "Id" integer NOT NULL,
                           "Identity" TEXT,
                           "IdentityName" TEXT,
                           "Passphrase" TEXT,
                           "KeySize" integer,
                           "SecretKey" TEXT,
                           "PublicKey" TEXT,
                           "ValidDurationBeginsOn" TEXT,
                           "ValidDurationEndsOn" TEXT,
                           "ValidDurationSeconds" integer,
                           "Description" TEXT,
                           "FileNamePublic" TEXT,
                           "FileNamePrivate" TEXT,
                           "Md5ValuePublic" TEXT,
                           "Md5ValuePrivate" TEXT,
                           "SignaturePublic" TEXT,
                           "SignaturePrivate" TEXT,
                           "FileCreateTime" TEXT,
                           "Type" TEXT,
                           "CreateTime" TEXT,
                           "UpdateTime" TEXT,
                           PRIMARY KEY ("Id")
);

-- 文件加密历史记录表
CREATE TABLE
    IF NOT EXISTS "GpgFileHistory" (
                                "Id" integer NOT NULL,
                                "OriginalFileName" TEXT,
                                "EncryptedFileName" TEXT,
                                "PublicKeyName" TEXT,
                                "PrivateKeyName" TEXT,
                                "EncryptionDate" TEXT,
                                "FileType" TEXT,
                                "CreateTime" TEXT,
                                "UpdateTime" TEXT,
                                PRIMARY KEY ("Id")
    );