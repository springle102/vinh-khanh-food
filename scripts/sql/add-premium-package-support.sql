SET NOCOUNT ON;
GO

/*
    Premium package support
    - CustomerUsers.IsPremium
    - SystemSettings.PremiumUnlockPriceUsd
    - SystemSettingLanguages fixed package policy
*/

IF OBJECT_ID(N'dbo.CustomerUsers', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.CustomerUsers', N'IsPremium') IS NULL
    BEGIN
        ALTER TABLE dbo.CustomerUsers
        ADD IsPremium BIT NULL;
    END;

    UPDATE dbo.CustomerUsers
    SET IsPremium = 0
    WHERE IsPremium IS NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.default_object_id = dc.object_id
        INNER JOIN sys.tables t
            ON t.object_id = c.object_id
        WHERE t.name = N'CustomerUsers'
          AND c.name = N'IsPremium')
    BEGIN
        ALTER TABLE dbo.CustomerUsers
        ADD CONSTRAINT DF_CustomerUsers_IsPremium DEFAULT ((0)) FOR IsPremium;
    END;

    ALTER TABLE dbo.CustomerUsers
    ALTER COLUMN IsPremium BIT NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemSettings (
        Id INT NOT NULL PRIMARY KEY,
        AppName NVARCHAR(200) NOT NULL,
        SupportEmail NVARCHAR(200) NOT NULL,
        DefaultLanguage NVARCHAR(20) NOT NULL,
        FallbackLanguage NVARCHAR(20) NOT NULL,
        PremiumUnlockPriceUsd INT NOT NULL,
        MapProvider NVARCHAR(50) NOT NULL,
        StorageProvider NVARCHAR(50) NOT NULL,
        TtsProvider NVARCHAR(50) NOT NULL,
        GeofenceRadiusMeters INT NOT NULL,
        GuestReviewEnabled BIT NOT NULL,
        AnalyticsRetentionDays INT NOT NULL
    );
END;
GO

IF COL_LENGTH(N'dbo.SystemSettings', N'PremiumUnlockPriceUsd') IS NULL
BEGIN
    ALTER TABLE dbo.SystemSettings
    ADD PremiumUnlockPriceUsd INT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE Id = 1)
BEGIN
    INSERT INTO dbo.SystemSettings (
        Id,
        AppName,
        SupportEmail,
        DefaultLanguage,
        FallbackLanguage,
        PremiumUnlockPriceUsd,
        MapProvider,
        StorageProvider,
        TtsProvider,
        GeofenceRadiusMeters,
        GuestReviewEnabled,
        AnalyticsRetentionDays)
    VALUES (
        1,
        N'Vinh Khanh Food Street',
        N'support@vinhkhanh.vn',
        N'vi',
        N'en',
        10,
        N'openstreetmap',
        N'cloudinary',
        N'elevenlabs',
        60,
        1,
        180);
END;
GO

UPDATE dbo.SystemSettings
SET PremiumUnlockPriceUsd = 10
WHERE PremiumUnlockPriceUsd IS NULL
   OR PremiumUnlockPriceUsd <= 0;
GO

ALTER TABLE dbo.SystemSettings
ALTER COLUMN PremiumUnlockPriceUsd INT NOT NULL;
GO

IF OBJECT_ID(N'dbo.SystemSettingLanguages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemSettingLanguages (
        SettingId INT NOT NULL,
        LanguageType NVARCHAR(20) NOT NULL,
        LanguageCode NVARCHAR(20) NOT NULL,
        PRIMARY KEY (SettingId, LanguageType, LanguageCode),
        CONSTRAINT FK_SystemSettingLanguages_SystemSettings
            FOREIGN KEY (SettingId) REFERENCES dbo.SystemSettings(Id)
    );
END;
GO

DELETE FROM dbo.SystemSettingLanguages
WHERE LanguageType IN (N'free', N'premium');
GO

INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode)
SELECT s.Id, v.LanguageType, v.LanguageCode
FROM dbo.SystemSettings s
CROSS APPLY (
    VALUES
        (N'free', N'vi'),
        (N'free', N'en'),
        (N'premium', N'zh-CN'),
        (N'premium', N'ko'),
        (N'premium', N'ja')
) v(LanguageType, LanguageCode);
GO
