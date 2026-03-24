IF DB_ID(N'VinhKhanhFoodAdmin') IS NULL
BEGIN
    RAISERROR(N'Database [VinhKhanhFoodAdmin] does not exist.', 16, 1);
    RETURN;
END;
GO

USE [VinhKhanhFoodAdmin];
GO

SET NOCOUNT ON;
GO

PRINT N'1. Remove deprecated QR schema';
GO

IF OBJECT_ID(N'dbo.QRCodes', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.QRCodes;
END;
GO

DECLARE @QrAutoPlayDefault NVARCHAR(128);
SELECT @QrAutoPlayDefault = dc.name
FROM sys.default_constraints dc
INNER JOIN sys.columns c
    ON c.default_object_id = dc.object_id
WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SystemSettings')
  AND c.name = N'QrAutoPlay';

IF @QrAutoPlayDefault IS NOT NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.SystemSettings DROP CONSTRAINT [' + @QrAutoPlayDefault + N'];');
END;

IF COL_LENGTH(N'dbo.SystemSettings', N'QrAutoPlay') IS NOT NULL
BEGIN
    ALTER TABLE dbo.SystemSettings DROP COLUMN QrAutoPlay;
END;
GO

PRINT N'2. Rename legacy Place tables to POI tables';
GO

IF OBJECT_ID(N'dbo.Places', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Pois', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.Places', N'Pois';
END;
GO

IF OBJECT_ID(N'dbo.CustomerFavoritePlaces', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.CustomerFavoritePlaces', N'CustomerFavoritePois';
END;
ELSE IF OBJECT_ID(N'dbo.CustomerFavoritePlaces', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.CustomerFavoritePlaces;
END;
GO

IF OBJECT_ID(N'dbo.PlaceTags', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.PoiTags', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.PlaceTags', N'PoiTags';
END;
ELSE IF OBJECT_ID(N'dbo.PlaceTags', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.PoiTags', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.PlaceTags;
END;
GO

IF OBJECT_ID(N'dbo.PlaceTranslations', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.PlaceTranslations', N'PoiTranslations';
END;
ELSE IF OBJECT_ID(N'dbo.PlaceTranslations', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.PlaceTranslations;
END;
GO

PRINT N'3. Rename legacy columns';
GO

IF COL_LENGTH(N'dbo.AdminUsers', N'ManagedPlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.AdminUsers', N'ManagedPoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.AdminUsers.ManagedPlaceId', N'ManagedPoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.CustomerFavoritePois', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.CustomerFavoritePois', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.CustomerFavoritePois.PlaceId', N'PoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.PoiTags', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.PoiTags', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.PoiTags.PlaceId', N'PoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.FoodItems', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.FoodItems', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.FoodItems.PlaceId', N'PoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.RouteStops', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.RouteStops', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.RouteStops.PlaceId', N'PoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.Promotions', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.Promotions', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.Promotions.PlaceId', N'PoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.Reviews', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.Reviews', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.Reviews.PlaceId', N'PoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.ViewLogs', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.ViewLogs', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.ViewLogs.PlaceId', N'PoiId', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.AudioListenLogs', N'PlaceId') IS NOT NULL AND COL_LENGTH(N'dbo.AudioListenLogs', N'PoiId') IS NULL
BEGIN
    EXEC sp_rename N'dbo.AudioListenLogs.PlaceId', N'PoiId', N'COLUMN';
END;
GO

PRINT N'4. Create missing POI tables if needed';
GO

IF OBJECT_ID(N'dbo.Pois', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Pois (
        Id NVARCHAR(50) NOT NULL PRIMARY KEY,
        Slug NVARCHAR(150) NOT NULL,
        AddressLine NVARCHAR(300) NOT NULL,
        Latitude DECIMAL(9,6) NOT NULL,
        Longitude DECIMAL(9,6) NOT NULL,
        CategoryId NVARCHAR(50) NOT NULL,
        [Status] NVARCHAR(30) NOT NULL,
        IsFeatured BIT NOT NULL,
        DefaultLanguageCode NVARCHAR(20) NOT NULL,
        District NVARCHAR(100) NOT NULL,
        Ward NVARCHAR(100) NOT NULL,
        PriceRange NVARCHAR(100) NOT NULL,
        AverageVisitDurationMinutes INT NOT NULL,
        PopularityScore INT NOT NULL,
        OwnerUserId NVARCHAR(50) NULL,
        UpdatedBy NVARCHAR(120) NOT NULL,
        CreatedAt DATETIMEOFFSET(7) NOT NULL,
        UpdatedAt DATETIMEOFFSET(7) NOT NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerFavoritePois (
        CustomerUserId NVARCHAR(50) NOT NULL,
        PoiId NVARCHAR(50) NOT NULL,
        PRIMARY KEY (CustomerUserId, PoiId)
    );
END;
GO

IF OBJECT_ID(N'dbo.PoiTags', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PoiTags (
        PoiId NVARCHAR(50) NOT NULL,
        TagValue NVARCHAR(100) NOT NULL,
        PRIMARY KEY (PoiId, TagValue)
    );
END;
GO

IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PoiTranslations (
        Id NVARCHAR(50) NOT NULL PRIMARY KEY,
        EntityType NVARCHAR(30) NOT NULL,
        EntityId NVARCHAR(50) NOT NULL,
        LanguageCode NVARCHAR(20) NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        ShortText NVARCHAR(MAX) NOT NULL,
        FullText NVARCHAR(MAX) NOT NULL,
        SeoTitle NVARCHAR(200) NOT NULL,
        SeoDescription NVARCHAR(MAX) NOT NULL,
        IsPremium BIT NOT NULL,
        UpdatedBy NVARCHAR(120) NOT NULL,
        UpdatedAt DATETIMEOFFSET(7) NOT NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.RefreshSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshSessions (
        RefreshToken NVARCHAR(200) NOT NULL PRIMARY KEY,
        UserId NVARCHAR(50) NOT NULL,
        ExpiresAt DATETIMEOFFSET(7) NOT NULL
    );
END;
GO

PRINT N'5. Drop legacy constraint names';
GO

IF OBJECT_ID(N'dbo.Pois', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Places_Categories')
        ALTER TABLE dbo.Pois DROP CONSTRAINT FK_Places_Categories;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Places_AdminUsers')
        ALTER TABLE dbo.Pois DROP CONSTRAINT FK_Places_AdminUsers;
END;
GO

IF OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerFavoritePlaces_CustomerUsers')
        ALTER TABLE dbo.CustomerFavoritePois DROP CONSTRAINT FK_CustomerFavoritePlaces_CustomerUsers;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerFavoritePlaces_Places')
        ALTER TABLE dbo.CustomerFavoritePois DROP CONSTRAINT FK_CustomerFavoritePlaces_Places;
END;
GO

IF OBJECT_ID(N'dbo.PoiTags', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PlaceTags_Places')
        ALTER TABLE dbo.PoiTags DROP CONSTRAINT FK_PlaceTags_Places;
END;
GO

IF OBJECT_ID(N'dbo.FoodItems', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_FoodItems_Places')
        ALTER TABLE dbo.FoodItems DROP CONSTRAINT FK_FoodItems_Places;
END;
GO

IF OBJECT_ID(N'dbo.RouteStops', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RouteStops_Places')
        ALTER TABLE dbo.RouteStops DROP CONSTRAINT FK_RouteStops_Places;
END;
GO

IF OBJECT_ID(N'dbo.Promotions', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Promotions_Places')
        ALTER TABLE dbo.Promotions DROP CONSTRAINT FK_Promotions_Places;
END;
GO

IF OBJECT_ID(N'dbo.Reviews', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Reviews_Places')
        ALTER TABLE dbo.Reviews DROP CONSTRAINT FK_Reviews_Places;
END;
GO

IF OBJECT_ID(N'dbo.ViewLogs', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ViewLogs_Places')
        ALTER TABLE dbo.ViewLogs DROP CONSTRAINT FK_ViewLogs_Places;
END;
GO

IF OBJECT_ID(N'dbo.AudioListenLogs', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AudioListenLogs_Places')
        ALTER TABLE dbo.AudioListenLogs DROP CONSTRAINT FK_AudioListenLogs_Places;
END;
GO

IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_PlaceTranslations')
        ALTER TABLE dbo.PoiTranslations DROP CONSTRAINT UQ_PlaceTranslations;
END;
GO

PRINT N'6. Add POI constraints';
GO

IF OBJECT_ID(N'dbo.Pois', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Pois_Categories')
        ALTER TABLE dbo.Pois ADD CONSTRAINT FK_Pois_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(Id);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Pois_AdminUsers')
        ALTER TABLE dbo.Pois ADD CONSTRAINT FK_Pois_AdminUsers FOREIGN KEY (OwnerUserId) REFERENCES dbo.AdminUsers(Id);
END;
GO

IF OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerFavoritePois_CustomerUsers')
        ALTER TABLE dbo.CustomerFavoritePois ADD CONSTRAINT FK_CustomerFavoritePois_CustomerUsers FOREIGN KEY (CustomerUserId) REFERENCES dbo.CustomerUsers(Id);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerFavoritePois_Pois')
        ALTER TABLE dbo.CustomerFavoritePois ADD CONSTRAINT FK_CustomerFavoritePois_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.PoiTags', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PoiTags_Pois')
        ALTER TABLE dbo.PoiTags ADD CONSTRAINT FK_PoiTags_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.FoodItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FoodItems', N'PoiId') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_FoodItems_Pois')
        ALTER TABLE dbo.FoodItems ADD CONSTRAINT FK_FoodItems_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_PoiTranslations')
        ALTER TABLE dbo.PoiTranslations ADD CONSTRAINT UQ_PoiTranslations UNIQUE (EntityType, EntityId, LanguageCode);
END;
GO

IF OBJECT_ID(N'dbo.RouteStops', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RouteStops', N'PoiId') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RouteStops_Pois')
        ALTER TABLE dbo.RouteStops ADD CONSTRAINT FK_RouteStops_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.Promotions', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Promotions', N'PoiId') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Promotions_Pois')
        ALTER TABLE dbo.Promotions ADD CONSTRAINT FK_Promotions_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.Reviews', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Reviews', N'PoiId') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Reviews_Pois')
        ALTER TABLE dbo.Reviews ADD CONSTRAINT FK_Reviews_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.ViewLogs', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ViewLogs', N'PoiId') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ViewLogs_Pois')
        ALTER TABLE dbo.ViewLogs ADD CONSTRAINT FK_ViewLogs_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.AudioListenLogs', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AudioListenLogs', N'PoiId') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AudioListenLogs_Pois')
        ALTER TABLE dbo.AudioListenLogs ADD CONSTRAINT FK_AudioListenLogs_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
END;
GO

IF OBJECT_ID(N'dbo.RefreshSessions', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RefreshSessions_AdminUsers')
        ALTER TABLE dbo.RefreshSessions DROP CONSTRAINT FK_RefreshSessions_AdminUsers;

    ALTER TABLE dbo.RefreshSessions
    ADD CONSTRAINT FK_RefreshSessions_AdminUsers FOREIGN KEY (UserId) REFERENCES dbo.AdminUsers(Id);
END;
GO

PRINT N'7. Drop legacy tables left over after POI migration';
GO

IF OBJECT_ID(N'dbo.CustomerFavoritePlaces', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.CustomerFavoritePlaces;
END;
GO

IF OBJECT_ID(N'dbo.PlaceTags', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.PlaceTags;
END;
GO

IF OBJECT_ID(N'dbo.PlaceTranslations', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.PlaceTranslations;
END;
GO

IF OBJECT_ID(N'dbo.QRCodes', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.QRCodes;
END;
GO

IF OBJECT_ID(N'dbo.Places', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.Places;
END;
GO

PRINT N'POI migration completed.';
GO
