IF DB_ID(N'VinhKhanhFoodAdmin') IS NULL
BEGIN
    EXEC(N'CREATE DATABASE [VinhKhanhFoodAdmin]');
END;
GO

USE [VinhKhanhFoodAdmin];
GO

-- Seed nay duoc dong bo tu SQL Server live cho cac bang noi dung loi.
-- Khong seed lai log/session va khong seed AudioGuides vi workspace hien khong co storage/audio tuong ung.
-- Cac ngon ngu khac duoc backend dich runtime theo languageCode.
IF OBJECT_ID(N'dbo.SystemSettingLanguages', N'U') IS NOT NULL DROP TABLE dbo.SystemSettingLanguages;
IF OBJECT_ID(N'dbo.RefreshSessions', N'U') IS NOT NULL DROP TABLE dbo.RefreshSessions;
IF OBJECT_ID(N'dbo.PremiumPurchaseTransactions', N'U') IS NOT NULL DROP TABLE dbo.PremiumPurchaseTransactions;
IF OBJECT_ID(N'dbo.CustomerFavoritePlaces', N'U') IS NOT NULL DROP TABLE dbo.CustomerFavoritePlaces;
IF OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NOT NULL DROP TABLE dbo.CustomerFavoritePois;
IF OBJECT_ID(N'dbo.UserActivityLogs', N'U') IS NOT NULL DROP TABLE dbo.UserActivityLogs;
IF OBJECT_ID(N'dbo.UserPoiVisits', N'U') IS NOT NULL DROP TABLE dbo.UserPoiVisits;
IF OBJECT_ID(N'dbo.AdminAuditLogs', N'U') IS NOT NULL DROP TABLE dbo.AdminAuditLogs;
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL DROP TABLE dbo.AuditLogs;
IF OBJECT_ID(N'dbo.RouteStops', N'U') IS NOT NULL DROP TABLE dbo.RouteStops;
IF OBJECT_ID(N'dbo.Routes', N'U') IS NOT NULL DROP TABLE dbo.Routes;
IF OBJECT_ID(N'dbo.PlaceTags', N'U') IS NOT NULL DROP TABLE dbo.PlaceTags;
IF OBJECT_ID(N'dbo.PoiTags', N'U') IS NOT NULL DROP TABLE dbo.PoiTags;
IF OBJECT_ID(N'dbo.AudioListenLogs', N'U') IS NOT NULL DROP TABLE dbo.AudioListenLogs;
IF OBJECT_ID(N'dbo.ViewLogs', N'U') IS NOT NULL DROP TABLE dbo.ViewLogs;
IF OBJECT_ID(N'dbo.AppUsageEvents', N'U') IS NOT NULL DROP TABLE dbo.AppUsageEvents;
IF OBJECT_ID(N'dbo.Reviews', N'U') IS NOT NULL DROP TABLE dbo.Reviews;
IF OBJECT_ID(N'dbo.Promotions', N'U') IS NOT NULL DROP TABLE dbo.Promotions;
IF OBJECT_ID(N'dbo.MediaAssets', N'U') IS NOT NULL DROP TABLE dbo.MediaAssets;
IF OBJECT_ID(N'dbo.AudioGuides', N'U') IS NOT NULL DROP TABLE dbo.AudioGuides;
IF OBJECT_ID(N'dbo.PlaceTranslations', N'U') IS NOT NULL DROP TABLE dbo.PlaceTranslations;
IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL DROP TABLE dbo.PoiTranslations;
IF OBJECT_ID(N'dbo.FoodItems', N'U') IS NOT NULL DROP TABLE dbo.FoodItems;
IF OBJECT_ID(N'dbo.QRCodes', N'U') IS NOT NULL DROP TABLE dbo.QRCodes;
IF OBJECT_ID(N'dbo.Places', N'U') IS NOT NULL DROP TABLE dbo.Places;
IF OBJECT_ID(N'dbo.Pois', N'U') IS NOT NULL DROP TABLE dbo.Pois;
IF OBJECT_ID(N'dbo.CustomerUsers', N'U') IS NOT NULL DROP TABLE dbo.CustomerUsers;
IF OBJECT_ID(N'dbo.AdminUsers', N'U') IS NOT NULL DROP TABLE dbo.AdminUsers;
IF OBJECT_ID(N'dbo.Categories', N'U') IS NOT NULL DROP TABLE dbo.Categories;
IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NOT NULL DROP TABLE dbo.SystemSettings;
GO

CREATE TABLE dbo.Categories (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Slug NVARCHAR(120) NOT NULL,
    Icon NVARCHAR(50) NOT NULL,
    Color NVARCHAR(20) NOT NULL
);
GO

CREATE TABLE dbo.AdminUsers (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    Name NVARCHAR(120) NOT NULL,
    Email NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(30) NOT NULL,
    Role NVARCHAR(30) NOT NULL,
    [Password] NVARCHAR(200) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    LastLoginAt DATETIMEOFFSET(7) NULL,
    AvatarColor NVARCHAR(20) NOT NULL,
    ManagedPoiId NVARCHAR(50) NULL,
    ApprovalStatus NVARCHAR(20) NOT NULL,
    RejectionReason NVARCHAR(1000) NULL,
    RegistrationSubmittedAt DATETIMEOFFSET(7) NULL,
    RegistrationReviewedAt DATETIMEOFFSET(7) NULL
);
GO

CREATE TABLE dbo.Pois (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    Slug NVARCHAR(150) NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    ShortDescription NVARCHAR(MAX) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    AudioScript NVARCHAR(MAX) NOT NULL,
    SourceLanguageCode NVARCHAR(20) NOT NULL,
    AddressLine NVARCHAR(300) NOT NULL,
    Latitude DECIMAL(9,6) NOT NULL,
    Longitude DECIMAL(9,6) NOT NULL,
    CategoryId NVARCHAR(50) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    IsFeatured BIT NOT NULL,
    District NVARCHAR(100) NOT NULL,
    Ward NVARCHAR(100) NOT NULL,
    PriceRange NVARCHAR(100) NOT NULL,
    TriggerRadius INT NOT NULL CONSTRAINT DF_Pois_TriggerRadius DEFAULT (20),
    Priority INT NOT NULL CONSTRAINT DF_Pois_Priority DEFAULT (0),
    OwnerUserId NVARCHAR(50) NULL,
    UpdatedBy NVARCHAR(120) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    IsActive BIT NOT NULL,
    LockedBySuperAdmin BIT NOT NULL,
    ApprovedAt DATETIMEOFFSET(7) NULL,
    RejectionReason NVARCHAR(1000) NULL,
    RejectedAt DATETIMEOFFSET(7) NULL,
    CONSTRAINT FK_Pois_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(Id),
    CONSTRAINT FK_Pois_AdminUsers FOREIGN KEY (OwnerUserId) REFERENCES dbo.AdminUsers(Id)
);
GO

CREATE TABLE dbo.PoiTags (
    PoiId NVARCHAR(50) NOT NULL,
    TagValue NVARCHAR(100) NOT NULL,
    PRIMARY KEY (PoiId, TagValue),
    CONSTRAINT FK_PoiTags_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE TABLE dbo.FoodItems (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PoiId NVARCHAR(50) NOT NULL,
    Name NVARCHAR(150) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    PriceRange NVARCHAR(100) NOT NULL,
    ImageUrl NVARCHAR(500) NOT NULL,
    CONSTRAINT FK_FoodItems_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

-- Deprecated: runtime khong doc/ghi ban dich vao bang nay nua. Giu schema rong de tuong thich migration cu.
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
    SourceLanguageCode NVARCHAR(20) NULL,
    SourceHash NVARCHAR(128) NULL,
    SourceUpdatedAt DATETIMEOFFSET(7) NULL,
    UpdatedBy NVARCHAR(120) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT UQ_PoiTranslations UNIQUE (EntityType, EntityId, LanguageCode)
);
GO

CREATE TABLE dbo.AudioGuides (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    EntityType NVARCHAR(30) NOT NULL,
    EntityId NVARCHAR(50) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    AudioUrl NVARCHAR(500) NOT NULL,
    TranscriptText NVARCHAR(MAX) NOT NULL DEFAULT N'',
    AudioFilePath NVARCHAR(500) NOT NULL DEFAULT N'',
    AudioFileName NVARCHAR(260) NOT NULL DEFAULT N'',
    VoiceType NVARCHAR(30) NOT NULL,
    SourceType NVARCHAR(30) NOT NULL,
    Provider NVARCHAR(50) NOT NULL DEFAULT N'elevenlabs',
    VoiceId NVARCHAR(120) NOT NULL DEFAULT N'',
    ModelId NVARCHAR(120) NOT NULL DEFAULT N'eleven_multilingual_v2',
    OutputFormat NVARCHAR(50) NOT NULL DEFAULT N'mp3_44100_128',
    DurationInSeconds FLOAT NULL,
    FileSizeBytes BIGINT NULL,
    TextHash NVARCHAR(128) NOT NULL DEFAULT N'',
    ContentVersion NVARCHAR(128) NOT NULL DEFAULT N'',
    GeneratedAt DATETIMEOFFSET(7) NULL,
    GenerationStatus NVARCHAR(30) NOT NULL DEFAULT N'none',
    ErrorMessage NVARCHAR(2000) NULL,
    IsOutdated BIT NOT NULL DEFAULT 0,
    [Status] NVARCHAR(30) NOT NULL,
    UpdatedBy NVARCHAR(120) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT UQ_AudioGuides UNIQUE (EntityType, EntityId, LanguageCode)
);
GO

CREATE TABLE dbo.MediaAssets (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    EntityType NVARCHAR(30) NOT NULL,
    EntityId NVARCHAR(50) NOT NULL,
    MediaType NVARCHAR(30) NOT NULL,
    Url NVARCHAR(500) NOT NULL,
    AltText NVARCHAR(300) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL
);
GO

CREATE TABLE dbo.Routes (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    IsFeatured BIT NOT NULL,
    IsActive BIT NOT NULL,
    IsSystemRoute BIT NOT NULL,
    OwnerUserId NVARCHAR(50) NULL,
    UpdatedBy NVARCHAR(120) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_Routes_AdminUsers_OwnerUserId FOREIGN KEY (OwnerUserId) REFERENCES dbo.AdminUsers(Id)
);
GO

CREATE TABLE dbo.RouteStops (
    RouteId NVARCHAR(50) NOT NULL,
    StopOrder INT NOT NULL,
    PoiId NVARCHAR(50) NOT NULL,
    PRIMARY KEY (RouteId, StopOrder),
    CONSTRAINT FK_RouteStops_Routes FOREIGN KEY (RouteId) REFERENCES dbo.Routes(Id),
    CONSTRAINT FK_RouteStops_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE TABLE dbo.Promotions (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PoiId NVARCHAR(50) NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    StartAt DATETIMEOFFSET(7) NOT NULL,
    EndAt DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    CONSTRAINT FK_Promotions_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE TABLE dbo.ViewLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PoiId NVARCHAR(50) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    DeviceType NVARCHAR(20) NOT NULL,
    ViewedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_ViewLogs_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE TABLE dbo.AudioListenLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PoiId NVARCHAR(50) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    ListenedAt DATETIMEOFFSET(7) NOT NULL,
    DurationInSeconds INT NOT NULL,
    CONSTRAINT FK_AudioListenLogs_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE TABLE dbo.AppUsageEvents (
    Id NVARCHAR(60) NOT NULL PRIMARY KEY,
    EventType NVARCHAR(40) NOT NULL,
    PoiId NVARCHAR(50) NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    Platform NVARCHAR(20) NOT NULL,
    SessionId NVARCHAR(80) NOT NULL,
    Source NVARCHAR(60) NOT NULL,
    Metadata NVARCHAR(MAX) NOT NULL,
    DurationInSeconds INT NULL,
    OccurredAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_AppUsageEvents_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE INDEX IX_AppUsageEvents_OccurredAt
ON dbo.AppUsageEvents (OccurredAt DESC, EventType, PoiId, LanguageCode);
GO

CREATE TABLE dbo.AuditLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    ActorName NVARCHAR(120) NOT NULL,
    ActorRole NVARCHAR(30) NOT NULL,
    [Action] NVARCHAR(200) NOT NULL,
    TargetValue NVARCHAR(300) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL
);
GO

CREATE TABLE dbo.AdminAuditLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    ActorId NVARCHAR(50) NOT NULL,
    ActorName NVARCHAR(120) NOT NULL,
    ActorRole NVARCHAR(50) NOT NULL,
    ActorType NVARCHAR(30) NOT NULL,
    [Action] NVARCHAR(160) NOT NULL,
    [Module] NVARCHAR(60) NOT NULL,
    TargetId NVARCHAR(120) NOT NULL,
    TargetSummary NVARCHAR(300) NOT NULL,
    BeforeSummary NVARCHAR(MAX) NULL,
    AfterSummary NVARCHAR(MAX) NULL,
    SourceApp NVARCHAR(60) NOT NULL,
    LegacyAuditId NVARCHAR(50) NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL
);
GO

CREATE TABLE dbo.RefreshSessions (
    AccessToken NVARCHAR(200) NOT NULL,
    RefreshToken NVARCHAR(200) NOT NULL PRIMARY KEY,
    UserId NVARCHAR(50) NOT NULL,
    AccessTokenExpiresAt DATETIMEOFFSET(7) NOT NULL,
    ExpiresAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_RefreshSessions_AdminUsers FOREIGN KEY (UserId) REFERENCES dbo.AdminUsers(Id)
);
GO

CREATE UNIQUE INDEX UX_RefreshSessions_AccessToken ON dbo.RefreshSessions (AccessToken);
GO

CREATE TABLE dbo.SystemSettings (
    Id INT NOT NULL PRIMARY KEY,
    AppName NVARCHAR(200) NOT NULL,
    SupportEmail NVARCHAR(200) NOT NULL,
    DefaultLanguage NVARCHAR(20) NOT NULL,
    FallbackLanguage NVARCHAR(20) NOT NULL,
    MapProvider NVARCHAR(50) NOT NULL,
    StorageProvider NVARCHAR(50) NOT NULL,
    TtsProvider NVARCHAR(50) NOT NULL,
    GeofenceRadiusMeters INT NOT NULL,
    AnalyticsRetentionDays INT NOT NULL
);
GO

CREATE TABLE dbo.SystemSettingLanguages (
    SettingId INT NOT NULL,
    LanguageType NVARCHAR(20) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    PRIMARY KEY (SettingId, LanguageType, LanguageCode),
    CONSTRAINT FK_SystemSettingLanguages_SystemSettings FOREIGN KEY (SettingId) REFERENCES dbo.SystemSettings(Id)
);
GO

INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES
(),
(),
(),
();
GO

INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES
(),
(),
(),
(),
(),
();
GO

INSERT INTO dbo.Pois (Id, Slug, Title, ShortDescription, [Description], AudioScript, SourceLanguageCode, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, TriggerRadius, Priority, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt, IsActive, LockedBySuperAdmin, ApprovedAt, RejectionReason, RejectedAt) VALUES
(),
(),
(),
(),
();
GO

INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES
(),
(),
(),
(),
(),
(),
(),
(),
(),
(),
(),
(),
(),
(),
();
GO

INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl) VALUES
(),
(),
(),
(),
(),
(),
();
GO

INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES
(),
(),
(),
(),
();
GO

INSERT INTO dbo.Routes (Id, Name, [Description], IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt) VALUES
(),
(),
();
GO

INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES
(),
(),
(),
(),
(),
(),
(),
(),
();
GO

INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status]) VALUES
(),
();
GO

-- dbo.SystemSettings currently has no seed rows from live SQL Server snapshot.
GO

INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES
(),
(),
(),
(),
();
GO

