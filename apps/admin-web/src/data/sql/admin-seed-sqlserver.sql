IF DB_ID(N'VinhKhanhFoodAdmin') IS NULL
BEGIN
    EXEC(N'CREATE DATABASE [VinhKhanhFoodAdmin]');
END;
GO
USE [VinhKhanhFoodAdmin];
GO

IF OBJECT_ID(N'dbo.SystemSettingLanguages', N'U') IS NOT NULL
    DROP TABLE dbo.SystemSettingLanguages;
GO
IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NOT NULL
    DROP TABLE dbo.SystemSettings;
GO
IF OBJECT_ID(N'dbo.RefreshSessions', N'U') IS NOT NULL
    DROP TABLE dbo.RefreshSessions;
GO
IF OBJECT_ID(N'dbo.PremiumPurchaseTransactions', N'U') IS NOT NULL
    DROP TABLE dbo.PremiumPurchaseTransactions;
GO
-- Legacy cleanup for removed customer-account/mobile-auth tables from the old public app model.
IF OBJECT_ID(N'dbo.UserActivityLogs', N'U') IS NOT NULL
    DROP TABLE dbo.UserActivityLogs;
GO
IF OBJECT_ID(N'dbo.UserPoiVisits', N'U') IS NOT NULL
    DROP TABLE dbo.UserPoiVisits;
GO
IF OBJECT_ID(N'dbo.AdminAuditLogs', N'U') IS NOT NULL
    DROP TABLE dbo.AdminAuditLogs;
GO
IF OBJECT_ID(N'dbo.RouteStops', N'U') IS NOT NULL
    DROP TABLE dbo.RouteStops;
GO
IF OBJECT_ID(N'dbo.Routes', N'U') IS NOT NULL
    DROP TABLE dbo.Routes;
GO
IF OBJECT_ID(N'dbo.CustomerFavoritePlaces', N'U') IS NOT NULL
    DROP TABLE dbo.CustomerFavoritePlaces;
GO
IF OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NOT NULL
    DROP TABLE dbo.CustomerFavoritePois;
GO
IF OBJECT_ID(N'dbo.PlaceTags', N'U') IS NOT NULL
    DROP TABLE dbo.PlaceTags;
GO
IF OBJECT_ID(N'dbo.PoiTags', N'U') IS NOT NULL
    DROP TABLE dbo.PoiTags;
GO
IF OBJECT_ID(N'dbo.AudioListenLogs', N'U') IS NOT NULL
    DROP TABLE dbo.AudioListenLogs;
GO
IF OBJECT_ID(N'dbo.ViewLogs', N'U') IS NOT NULL
    DROP TABLE dbo.ViewLogs;
GO
IF OBJECT_ID(N'dbo.AppUsageEvents', N'U') IS NOT NULL
    DROP TABLE dbo.AppUsageEvents;
GO
IF OBJECT_ID(N'dbo.Reviews', N'U') IS NOT NULL
    DROP TABLE dbo.Reviews;
GO
IF OBJECT_ID(N'dbo.Promotions', N'U') IS NOT NULL
    DROP TABLE dbo.Promotions;
GO
IF OBJECT_ID(N'dbo.MediaAssets', N'U') IS NOT NULL
    DROP TABLE dbo.MediaAssets;
GO
IF OBJECT_ID(N'dbo.AudioGuides', N'U') IS NOT NULL
    DROP TABLE dbo.AudioGuides;
GO
IF OBJECT_ID(N'dbo.PlaceTranslations', N'U') IS NOT NULL
    DROP TABLE dbo.PlaceTranslations;
GO
IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
    DROP TABLE dbo.PoiTranslations;
GO
IF OBJECT_ID(N'dbo.FoodItems', N'U') IS NOT NULL
    DROP TABLE dbo.FoodItems;
GO
IF OBJECT_ID(N'dbo.QRCodes', N'U') IS NOT NULL
    DROP TABLE dbo.QRCodes;
GO
IF OBJECT_ID(N'dbo.Places', N'U') IS NOT NULL
    DROP TABLE dbo.Places;
GO
IF OBJECT_ID(N'dbo.Pois', N'U') IS NOT NULL
    DROP TABLE dbo.Pois;
GO
IF OBJECT_ID(N'dbo.CustomerUsers', N'U') IS NOT NULL
    DROP TABLE dbo.CustomerUsers;
GO
IF OBJECT_ID(N'dbo.AdminUsers', N'U') IS NOT NULL
    DROP TABLE dbo.AdminUsers;
GO
IF OBJECT_ID(N'dbo.Categories', N'U') IS NOT NULL
    DROP TABLE dbo.Categories;
GO
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
    DROP TABLE dbo.AuditLogs;
GO

-- ============================================
-- Tạo bảng
-- ============================================
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
    AddressLine NVARCHAR(300) NOT NULL,
    Latitude DECIMAL(9,6) NOT NULL,
    Longitude DECIMAL(9,6) NOT NULL,
    CategoryId NVARCHAR(50) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    IsFeatured BIT NOT NULL,
    District NVARCHAR(100) NOT NULL,
    Ward NVARCHAR(100) NOT NULL,
    PriceRange NVARCHAR(100) NOT NULL,
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
    SpicyLevel NVARCHAR(30) NOT NULL,
    CONSTRAINT FK_FoodItems_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

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
    VoiceType NVARCHAR(30) NOT NULL,
    SourceType NVARCHAR(30) NOT NULL,
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

CREATE TABLE dbo.Reviews (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PoiId NVARCHAR(50) NOT NULL,
    UserName NVARCHAR(120) NOT NULL,
    Rating INT NOT NULL,
    CommentText NVARCHAR(MAX) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    CONSTRAINT FK_Reviews_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id),
    CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5)
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

CREATE UNIQUE INDEX UX_AdminAuditLogs_LegacyAuditId
ON dbo.AdminAuditLogs (LegacyAuditId)
WHERE LegacyAuditId IS NOT NULL;
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

CREATE UNIQUE INDEX UX_RefreshSessions_AccessToken
ON dbo.RefreshSessions (AccessToken);
GO

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
GO

CREATE TABLE dbo.SystemSettingLanguages (
    SettingId INT NOT NULL,
    LanguageType NVARCHAR(20) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    PRIMARY KEY (SettingId, LanguageType, LanguageCode),
    CONSTRAINT FK_SystemSettingLanguages_SystemSettings FOREIGN KEY (SettingId) REFERENCES dbo.SystemSettings(Id)
);
GO

-- ============================================
-- Nạp dữ liệu mẫu
-- ============================================
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-bbq', N'Nướng & Beer', N'nuong-beer', N'flame', N'#de6245');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-coffee-tea', N'Cà phê & trà', N'ca-phe-tra', N'coffee', N'#8b5e3c');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-oc', N'Ốc & Hải sản', N'oc-hai-san', N'shell', N'#f97316');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-seafood-raw', N'Hải sản & đồ sống', N'hai-san-do-song', N'fish', N'#0f766e');
GO
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES (N'user-owner-bbq', N'Minh Ánh', N'minhanh@gmail.com', N'0909 188 002', N'PLACE_OWNER', N'Admin@123', N'active', CAST(N'2025-12-14T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-09T16:15:27.8173800+00:00' AS datetimeoffset(7)), N'#de6245', N'sushi-ko', N'approved', NULL, CAST(N'2025-12-14T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2025-12-14T02:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES (N'user-owner-dessert', N'Phạm Mỹ Linh', N'mylinh@gmail.com', N'0909 188 004', N'PLACE_OWNER', N'Admin@123', N'active', CAST(N'2026-01-05T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-12T07:20:00.0000000+00:00' AS datetimeoffset(7)), N'#7c2d12', N'ca-phe-che', N'approved', NULL, CAST(N'2026-01-05T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-01-05T02:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES (N'user-owner-oc', N'Lê Quốc Bảo', N'quocbao@gmail.com', N'0909 188 003', N'PLACE_OWNER', N'Admin@123', N'active', CAST(N'2025-12-29T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-09T16:19:46.2940904+00:00' AS datetimeoffset(7)), N'#d9a845', N'oc-phat', N'approved', NULL, CAST(N'2025-12-29T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2025-12-29T02:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES (N'user-f60f843a', N'Tiểu Mỹ', N'tieumy123@gmail.com', N'0952478925', N'PLACE_OWNER', N'tieumy@123', N'active', CAST(N'2026-04-13T16:37:52.3451137Z' AS datetimeoffset(7)), CAST(N'2026-04-13T16:39:49.0713407Z' AS datetimeoffset(7)), N'#f97316', NULL, N'approved', NULL, CAST(N'2026-04-13T16:37:52.3451137Z' AS datetimeoffset(7)), CAST(N'2026-04-13T16:39:15.3044520Z' AS datetimeoffset(7)));
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES (N'user-super', N'Ánh Xuân', N'anhxuan@gmail.com', N'0909 188 001', N'SUPER_ADMIN', N'Admin@123', N'active', CAST(N'2025-11-19T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-09T16:25:15.4511570+00:00' AS datetimeoffset(7)), N'#f97316', NULL, N'approved', NULL, CAST(N'2025-11-19T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2025-11-19T02:00:00.0000000+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.Pois (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt, IsActive, LockedBySuperAdmin, ApprovedAt, RejectionReason, RejectedAt) VALUES (N'ca-phe-che', N'ca-phe-che', N'Chung cư H1, Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.762195, 106.701499, N'cat-coffee-tea', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'25.000 - 75.000 VND', N'user-owner-dessert', N'Ánh Xuân', CAST(N'2026-01-16T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-06T06:57:06.9121038+00:00' AS datetimeoffset(7)), 1, 0, CAST(N'2026-04-06T06:57:06.9121038+00:00' AS datetimeoffset(7)), NULL, NULL);
INSERT INTO dbo.Pois (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt, IsActive, LockedBySuperAdmin, ApprovedAt, RejectionReason, RejectedAt) VALUES (N'oc-oanh-1', N'oc-oanh-1', N'Ốc Oanh 1, 534 Hẻm 42/237 Đường Hoàng Diệu, 534, Hẻm 42/237 Đường Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.761415, 106.702834, N'cat-oc', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'', N'user-f60f843a', N'Ánh Xuân', CAST(N'2026-04-13T16:45:22.3341660Z' AS datetimeoffset(7)), CAST(N'2026-04-13T16:46:02.6374502Z' AS datetimeoffset(7)), 1, 0, CAST(N'2026-04-13T16:46:02.6262194Z' AS datetimeoffset(7)), NULL, NULL);
INSERT INTO dbo.Pois (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt, IsActive, LockedBySuperAdmin, ApprovedAt, RejectionReason, RejectedAt) VALUES (N'oc-phat', N'oc-phat', N'Ốc Phát, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.761873, 106.702153, N'cat-oc', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'50.000-110.000', N'user-owner-oc', N'Ánh Xuân', CAST(N'2025-11-19T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-09T16:25:22.2941302+00:00' AS datetimeoffset(7)), 1, 0, CAST(N'2026-04-09T16:25:22.2941302+00:00' AS datetimeoffset(7)), NULL, NULL);
INSERT INTO dbo.Pois (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt, IsActive, LockedBySuperAdmin, ApprovedAt, RejectionReason, RejectedAt) VALUES (N'sushi-ko', N'nha-hang-sushi-ko', N'Nhà Hàng Sushi Ko, 122/37/15 Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.760772, 106.704798, N'cat-seafood-raw', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'50.000-150.000', N'user-owner-bbq', N'Ánh Xuân', CAST(N'2025-11-19T03:15:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-09T16:18:37.7903537+00:00' AS datetimeoffset(7)), 1, 0, CAST(N'2026-04-09T16:18:37.7903537+00:00' AS datetimeoffset(7)), NULL, NULL);
GO
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'ca-phe-che', N'ăn vặt');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'ca-phe-che', N'chè');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'ca-phe-che', N'gia đình');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'oc-oanh-1', N'gia đình');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'oc-oanh-1', N'hải sản');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'oc-oanh-1', N'ốc');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'oc-oanh-1', N'tụ tập');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'oc-phat', N'đặc sản');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'oc-phat', N'gia đình');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'oc-phat', N'ốc');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'sushi-ko', N'đồ sống');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'sushi-ko', N'hải sản');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'sushi-ko', N'sushi');
GO
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-1', N'oc-phat', N'Ốc móng tay xào/bơ/tỏi/me', N'Ốc móng tay ngon đậm vị.', N'79.000', N'http://localhost:5080/storage/images/food-items/20260409162352122-2185a13ab52b4c0aa10796eaf4b09cde.jpg', N'mild');
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-2', N'sushi-ko', N'Zaru Soba', N'Món mì kiều mạch lạnh truyền thống Nhật Bản, nổi bật với sợi mì dai ngon đặt trên rổ tre (zaru), thường được thưởng thức vào mùa hè.', N'142.000', N'http://localhost:5080/storage/images/food-items/20260409161059794-4f20083247d64e6e909c407faa693fe4.jpg', N'mild');
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-3', N'ca-phe-che', N'Chè khúc bạch nhiệt đới', N'Món tráng miệng mát lạnh phù hợp cho khách gia đình và trẻ em.', N'28.000 - 45.000 VND', N'https://images.unsplash.com/photo-1563805042-7684c019e1cb?auto=format&fit=crop&w=900&q=80', N'mild');
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-97a4f9c2', N'oc-oanh-1', N'Sò điệp nướng mỡ hàng', N'Sò điệp nướng mỡ hành là sự kết hợp hoàn hảo giữa cồi sò ngọt thanh, dai mềm và lớp mỡ hành béo ngậy tỏa hương thơm lừng trên lừa hồng. Điểm xuyết thêm chút đậu phộng rang giòn rụm và rưới đẫm nước mắm chua ngọt, món ăn này chắc chắn sẽ đánh thức mọi giác quan của bạn ngay từ miếng đầu tiên.', N'40.000', N'http://localhost:5080/storage/images/food-items/20260413164520076-e203444d968b4a76970f1f650ac22302.jpg', N'mild');
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-5f850dc5', N'sushi-ko', N'Kajiki Maguro Yaki', N'Món cá kiếm (Kajiki) nướng Nhật Bản, được ướp trong sốt Yūan-ji (nước tương, mirin, và rượu sake) cùng cam quýt (citrus).', N'138.000', N'http://localhost:5080/storage/images/food-items/20260409161752519-d02d6dd949d244959e96a94ceef3bef8.jpg', N'mild');
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-b61e8e41', N'oc-phat', N'Ốc giác xào rau muống', N'Ốc giác thơm ngon béo ngậy.', N'129.000', N'http://localhost:5080/storage/images/food-items/20260409162508676-576ff2b876444bb989ded5ed4784e074.jpg', N'mild');
GO
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-1', N'poi', N'sushi-ko', N'vi', N'Nhà Hàng Sushi Ko', N'', N'Chào mừng quý khách đến với Nhà Hàng Sushi Ko. Chúng tôi tự hào là điểm đến lý tưởng cho những ai yêu thích hương vị Sushi nguyên bản từ Nhật Bản. Tại đây, mỗi món ăn đều được chế biến từ nguồn hải sản tươi ngon nhất trong ngày, kết hợp cùng kỹ nghệ điêu luyện của các nghệ nhân tâm huyết. Chúc quý khách có một trải nghiệm ẩm thực tinh tế và ấm cúng trong không gian của chúng tôi.', N'Nhà Hàng Sushi Ko', N'Nhà Hàng Sushi Ko', 0, N'Ánh Xuân', CAST(N'2026-04-09T16:18:37.8559466+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-2', N'poi', N'sushi-ko', N'en', N'BBQ Night Culinary Square', N'A lively seafood grill stop with upbeat local night-street energy.', N'BBQ Night is one of the key anchors of Vinh Khanh Food Street, bringing together fresh grilled seafood, bright urban lights, and an informal local social vibe.', N'BBQ Night at Vinh Khanh Food Street', N'Seafood grill spot for evening food tours in District 4.', 0, N'Minh Ánh', CAST(N'2026-03-18T07:05:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-3', N'poi', N'oc-phat', N'vi', N'Ốc Phát', N'Quán ốc đặc trưng với thực đơn đa dạng, phù hợp khách lần đầu đến khu phố.', N'Bạn đang tìm một chân ái hải sản tại "Thủ phủ ốc" Quận 4? Không cần đi đâu xa, cứ nhắm ngay cổng chào Phố ẩm thực Vĩnh Khánh mà tấp vào Ốc Phát nhé!
Vì sao Ốc Phát luôn là toạ độ "nóng hổi" mỗi tối?

Vị trí đắc địa: Nằm ngay đầu cổng Vĩnh Khánh (số 361 - 363), mặt bằng siêu rộng rãi, thoáng mát, bao tụ tập nhóm đông hay tổ chức tiệc tùng.

Hải sản tươi rói, size khủng: Ốc hương to đùng rang muối đậm đà, nghêu hấp Thái cay nồng húp trọn nước, ốc tỏi nướng mỡ hành thơm nức mũi,... mẻ nào ra lò cũng chất lượng!

Tốc độ bàn thờ: Quán đông nhưng lên món cực nhanh, đội ngũ nhân viên nhiệt tình, không để anh em phải chờ đợi lâu trễ nhịp cụng ly.

Giá rổ bình dân: Chỉ từ 50k - 110k/món, ăn no căng rốn mà không lo "cháy túi".

Trời Sài Gòn dạo này mát mẻ, làm vài chai bia lạnh, nhâm nhi đĩa ốc móng tay xào rau muống thì còn gì bằng! Gọi ngay cho hội chị em bạn dì tối nay qua Ốc Phát "oanh tạc" thôi!', N'Ốc Phát', N'Quán ốc đặc trưng với thực đơn đa dạng, phù hợp khách lần đầu đến khu phố.', 0, N'Ánh Xuân', CAST(N'2026-04-09T16:25:22.3514437+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-4', N'poi', N'oc-phat', N'en', N'Vinh Khanh Signature Snail House', N'An iconic snail and shellfish stop for first-time visitors.', N'This destination captures District 4 street dining at its most energetic, with a broad seafood menu and quick service.', N'Signature Snail House in Vinh Khanh', N'Iconic seafood and snail experience for tourists and locals.', 0, N'Minh Ánh', CAST(N'2026-03-19T09:05:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-5', N'poi', N'ca-phe-che', N'vi', N'Cà phê Chè', N'', N'Gen Z bây giờ sống là phải rõ ràng, không thích mập mờ lòng vòng. Thèm cafe và chè thì đi đâu? Dĩ nhiên là đi... Cà Phê Chè rồi!

Tên quán ngắn gọn, súc tích nhưng menu thì lại "dài ngoằng" và chất lượng:
Giao diện: Xinh xắn, mát mẻ, góc nào đưa điện thoại lên cũng có ngay ảnh mang về.
Hệ điều hành cốt lõi: * Cà phê: Đậm đà, thơm lừng, đủ sức "độ" bạn qua những deadline dí ngập mặt.

Chè: Đỉnh cao của sự "chữa lành" với topping ngập tràn, nước cốt dừa béo ngậy, ngọt thanh không hề gắt cổ.

Tóm lại là, chiều nay lượn Quận 4, không cần phải suy nghĩ "Ăn gì? Uống gì?". Cứ thẳng tiến đến Cà Phê Chè là có đủ hết nha!', N'Cà phê Chè', N'Cà phê Chè', 0, N'Ánh Xuân', CAST(N'2026-04-06T06:57:07.0336613+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-d3f4f1e9', N'poi', N'oc-oanh-1', N'vi', N'Ốc Oanh 1', N'', N'Bạn đang dừng chân tại khu vực Quán Ốc Oanh, một trong những tọa độ ẩm thực không thể bỏ qua trên con đường Vĩnh Khánh sầm uất. Nơi đây nổi tiếng với quầy hải sản tươi sống luôn được bày biện bắt mắt ngay trước quán. Nếu bạn là tín đồ của các món ốc đậm vị Nam Bộ, hãy thử ngay Ốc hương xào tỏi thơm lừng hoặc Sò huyết rang me với lớp sốt chua ngọt sền sệt cực kỳ bắt miệng. Hãy chọn một bàn trống và chuẩn bị khám phá thiên đường ốc Quận 4 ngay thôi!', N'Ốc Oanh 1', N'Ốc Oanh 1', 0, N'Tiểu Mỹ', CAST(N'2026-04-13T16:46:02.6374502Z' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-c267c49e', N'poi', N'sushi-ko', N'zh-CN', N'Quảng Trường Ẩm thực BBQ Night', N'', N'烧烤之夜是永庆美食街的一大亮点，游客可以在这里通过烤海鲜的香气和热闹的当地氛围，体验西贡充满活力的街头生活。', N'', N'', 0, N'Ánh Xuân', CAST(N'2026-03-21T03:10:22.6481221+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-1', N'poi', N'sushi-ko', N'vi', N'', N'standard', N'tts', N'ready', N'Minh Ánh', CAST(N'2026-04-09T16:17:56.3843276+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-2', N'poi', N'sushi-ko', N'en', N'https://cdn.example.com/audio/bbq-night-en.mp3', N'standard', N'tts', N'ready', N'Minh Ánh', CAST(N'2026-03-18T07:20:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-3', N'poi', N'oc-phat', N'vi', N'', N'standard', N'tts', N'ready', N'Lê Quốc Bảo', CAST(N'2026-04-09T16:25:12.0639324+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-4', N'poi', N'ca-phe-che', N'vi', N'', N'standard', N'tts', N'ready', N'Ánh Xuân', CAST(N'2026-04-06T06:57:07.1390571+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-2b105ab9', N'poi', N'oc-oanh-1', N'vi', N'', N'standard', N'tts', N'ready', N'Tiểu Mỹ', CAST(N'2026-04-13T16:46:02.6374502Z' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-5f585e13', N'poi', N'sushi-ko', N'zh-CN', N'', N'south', N'tts', N'ready', N'Ánh Xuân', CAST(N'2026-03-21T03:10:22.6039699+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES (N'media-1', N'poi', N'sushi-ko', N'image', N'http://localhost:5080/storage/images/pois/20260409160718746-313ac5f0b5124cbfb8423cbb11c0682b.jpg', N'Không khí phố ẩm thực Vĩnh Khánh về đêm', CAST(N'2026-03-04T02:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES (N'media-2', N'poi', N'oc-phat', N'image', N'http://localhost:5080/storage/images/pois/20260409162031251-eb07e61f52d44dfdabda3b5daa1561dc.jpg', N'Đĩa ốc xào phục vụ tại quán', CAST(N'2026-03-03T02:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES (N'media-ab72fa4d', N'poi', N'oc-oanh-1', N'image', N'http://localhost:5080/storage/images/pois/20260413164216292-e84ebc53cf834425bd2e8cd33b886f3e.jpg', N'Ảnh đại diện Ốc Oanh 1', CAST(N'2026-04-13T16:45:22.7740238Z' AS datetimeoffset(7)));
GO
INSERT INTO dbo.Routes (Id, Name, [Description], IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt) VALUES (N'route-07ca13f3', N'Tour giải khát và trải nghiệm ốc', N'Khởi động nhẹ nhàng bằng một ly nước mát lạnh để "bôi trơn" thanh quản. Sau khi lấy đủ sức, anh em mình chính thức đổ bộ vào thiên đường ốc: từ ốc hương xào bơ tỏi thơm nức mũi, đến ốc móng tay xào rau muống đậm đà. Đủ combo ăn uống + chém gió. Lên đồ, chiều nay triển luôn!', 0, 1, 1, NULL, N'Ánh Xuân', CAST(N'2026-04-13T16:33:51.1349156Z' AS datetimeoffset(7)));
INSERT INTO dbo.Routes (Id, Name, [Description], IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt) VALUES (N'route-976b4e97', N'Tour ốc', N'Trải nghiệm văn hóa ăn vỉa hè đúng chất Sài Gòn với tour ẩm thực chuyên đề Ốc! Hòa mình vào không khí náo nhiệt, tiếng cụng ly lách cách và mùi thơm nức mũi của mỡ hành, sả ớt. Bạn sẽ được trực tiếp lựa chọn hải sản tươi sống và thưởng thức hàng chục phong cách chế biến cực kỳ bắt miệng. Một hành trình bùng nổ vị giác đang chờ bạn khám phá!', 0, 1, 1, NULL, N'Ánh Xuân', CAST(N'2026-04-13T16:48:07.6841364Z' AS datetimeoffset(7)));
GO
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (N'route-07ca13f3', 1, N'oc-phat');
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (N'route-07ca13f3', 2, N'ca-phe-che');
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (N'route-976b4e97', 1, N'oc-phat');
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (N'route-976b4e97', 2, N'oc-oanh-1');
GO
INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status]) VALUES (N'promo-1', N'sushi-ko', N'Combo BBQ nhóm 4 người', N'Tặng nước uống cho khách check-in POI trong khung giờ 18h-20h.', CAST(N'2026-03-18T11:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-25T14:00:00.0000000+00:00' AS datetimeoffset(7)), N'active');
INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status]) VALUES (N'promo-2', N'oc-phat', N'Giảm 10% cho khách nghe audio guide', N'Áp dụng khi khách hoàn tất bài thuyết minh tiếng Việt hoặc tiếng Anh.', CAST(N'2026-03-20T10:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-28T14:00:00.0000000+00:00' AS datetimeoffset(7)), N'upcoming');
GO
INSERT INTO dbo.Reviews (Id, PoiId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]) VALUES (N'review-1', N'oc-phat', N'Thu Trang', 5, N'Chạm vào POI trên bản đồ rồi nghe giới thiệu rất tiện và dễ hiểu.', N'vi', CAST(N'2026-03-18T05:00:00.0000000+00:00' AS datetimeoffset(7)), N'approved');
INSERT INTO dbo.Reviews (Id, PoiId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]) VALUES (N'review-2', N'sushi-ko', N'Lucas', 4, N'Useful overview before starting the food route.', N'en', CAST(N'2026-03-18T08:00:00.0000000+00:00' AS datetimeoffset(7)), N'pending');
GO
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-14', N'poi_view', N'sushi-ko', N'en', N'android', N'session-en-1901', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-19T09:40:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-12', N'poi_view', N'oc-phat', N'en', N'android', N'session-en-1802', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-18T18:25:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-11', N'poi_view', N'sushi-ko', N'vi', N'android', N'session-vi-1801', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-18T12:45:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-09', N'poi_view', N'oc-phat', N'ja', N'android', N'session-ja-1701', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-17T11:20:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-08', N'poi_view', N'sushi-ko', N'en', N'android', N'session-en-1601', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-16T19:40:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-06', N'poi_view', N'oc-phat', N'vi', N'android', N'session-vi-1502', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-15T18:30:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-05', N'poi_view', N'sushi-ko', N'zh-CN', N'android', N'session-zh-1501', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-15T13:10:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-02', N'poi_view', N'oc-phat', N'en', N'android', N'session-en-1301', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-13T12:20:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-poi-01', N'poi_view', N'sushi-ko', N'vi', N'android', N'session-vi-1301', N'poi_detail', N'{"entry":"map-pin"}', NULL, CAST(N'2026-03-13T11:10:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-audio-10', N'audio_play', N'sushi-ko', N'vi', N'android', N'session-vi-1901', N'audio_player', N'{"guideType":"poi"}', 118, CAST(N'2026-03-19T10:15:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-audio-08', N'audio_play', N'oc-phat', N'ja', N'android', N'session-ja-1801', N'audio_player', N'{"guideType":"poi"}', 95, CAST(N'2026-03-18T20:55:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-audio-07', N'audio_play', N'sushi-ko', N'en', N'android', N'session-en-1802', N'audio_player', N'{"guideType":"poi"}', 91, CAST(N'2026-03-18T20:20:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-audio-05', N'audio_play', N'oc-phat', N'vi', N'android', N'session-vi-1602', N'audio_player', N'{"guideType":"poi"}', 102, CAST(N'2026-03-16T18:15:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-audio-04', N'audio_play', N'sushi-ko', N'zh-CN', N'android', N'session-zh-1501', N'audio_player', N'{"guideType":"poi"}', 110, CAST(N'2026-03-15T19:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-audio-02', N'audio_play', N'oc-phat', N'en', N'android', N'session-en-1401', N'audio_player', N'{"guideType":"poi"}', 88, CAST(N'2026-03-14T12:10:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-audio-01', N'audio_play', N'sushi-ko', N'vi', N'android', N'session-vi-1301', N'audio_player', N'{"guideType":"poi"}', 96, CAST(N'2026-03-13T11:30:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-qr-01', N'qr_scan', N'sushi-ko', N'vi', N'android', N'session-vi-1301', N'qr_scanner', N'{"entry":"poi-qr"}', NULL, CAST(N'2026-03-13T11:08:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-qr-02', N'qr_scan', N'oc-phat', N'en', N'android', N'session-en-1802', N'qr_scanner', N'{"entry":"poi-qr"}', NULL, CAST(N'2026-03-18T18:21:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-qr-03', N'qr_scan', N'sushi-ko', N'zh-CN', N'android', N'session-zh-1501', N'qr_scanner', N'{"entry":"poi-qr"}', NULL, CAST(N'2026-03-15T13:05:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AppUsageEvents (Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt) VALUES (N'usage-qr-04', N'qr_scan', N'oc-phat', N'ja', N'android', N'session-ja-1701', N'qr_scanner', N'{"entry":"poi-qr"}', NULL, CAST(N'2026-03-17T11:18:00.0000000+00:00' AS datetimeoffset(7)));
GO

INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt)
SELECT
    CONCAT(N'view-', RIGHT(event.Id, 12)),
    event.PoiId,
    event.LanguageCode,
    event.Platform,
    event.OccurredAt
FROM dbo.AppUsageEvents event
WHERE event.EventType = N'poi_view'
  AND event.PoiId IS NOT NULL;
GO

INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds)
SELECT
    CONCAT(N'listen-', RIGHT(event.Id, 12)),
    event.PoiId,
    event.LanguageCode,
    event.OccurredAt,
    COALESCE(event.DurationInSeconds, 0)
FROM dbo.AppUsageEvents event
WHERE event.EventType = N'audio_play'
  AND event.PoiId IS NOT NULL;
GO

INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3afc4b8c', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T07:32:14.4401076+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3e263be7', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T07:32:10.9270794+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-87f6a364', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T07:32:10.9127857+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-26d710ce', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:ca-phe-che', CAST(N'2026-04-06T06:57:07.1456732+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-d9ccb4f4', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:ca-phe-che', CAST(N'2026-04-06T06:57:07.0425063+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-96e912d7', N'Ánh Xuân', N'SUPER_ADMIN', N'Duyệt POI', N'ca-phe-che', CAST(N'2026-04-06T06:57:07.0072985+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-e89dfe33', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:oc-phat', CAST(N'2026-04-06T06:50:09.7705929+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-38fa365c', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:oc-phat', CAST(N'2026-04-06T06:50:09.6706293+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-e1209bcd', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật POI', N'oc-phat', CAST(N'2026-04-06T06:50:09.6277469+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-1bcf7881', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T06:43:15.2353236+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-ba5b8b60', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T06:43:14.7629748+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-762421ad', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T06:43:14.7511854+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-951d289a', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T06:37:33.6903558+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-858a1d4c', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T06:37:33.5173626+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-1c27af6e', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-06T06:37:33.4907142+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-a6652bc2', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T15:22:25.3770840+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-957e3b51', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T15:22:25.1996853+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-fa3356d9', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T15:22:25.1811823+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-38d2df79', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:08:02.8445817+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-0d087739', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:08:02.6802305+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-e7b8e5b2', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:08:02.6656931+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3bfe7099', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:poi-snail-signature', CAST(N'2026-04-05T11:05:53.4134133+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-26a99f55', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:poi-snail-signature', CAST(N'2026-04-05T11:05:53.3547915+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-fd9e35d9', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật POI', N'quan-oc-vinh-khanh-signature', CAST(N'2026-04-05T11:05:53.2925650+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-db11dc14', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:poi-snail-signature', CAST(N'2026-04-05T11:05:40.1974212+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-596c8df0', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:poi-snail-signature', CAST(N'2026-04-05T11:05:40.0722093+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-d096182d', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật POI', N'quan-oc-vinh-khanh-signature', CAST(N'2026-04-05T11:05:40.0159470+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-66b7f99c', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:04:18.5019735+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-10460240', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:04:16.8524932+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-f891f061', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:04:16.8342565+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-204afd73', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:01:56.6174628+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-5299f672', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:01:56.4583595+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-698eb870', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T11:01:56.4351580+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-d9c6de1c', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T10:57:34.6552016+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-63d45384', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T10:57:34.4580715+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-63f43baf', N'Ánh Xuân', N'SUPER_ADMIN', N'Làm mới phiên đăng nhập', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T10:57:34.4438855+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-fb9e0f95', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:sushi-ko', CAST(N'2026-04-05T10:24:38.7528490+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-9fc7d48f', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:sushi-ko', CAST(N'2026-04-05T10:24:38.6712216+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-72ffb498', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật POI', N'nha-hang-sushi-ko', CAST(N'2026-04-05T10:24:38.6332434+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-d1c29436', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:sushi-ko', CAST(N'2026-04-05T10:23:48.9715670+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-f62af574', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:sushi-ko', CAST(N'2026-04-05T10:23:48.8188478+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-ba698e22', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật POI', N'nha-hang-sushi-ko', CAST(N'2026-04-05T10:23:48.7418673+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-b5ee5f39', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-04-05T10:21:51.3752086+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-bd53c6fe', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:18:50.2650712+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-9a4656f9', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:14:27.3125511+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-b368a6a2', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:poi-bbq-night', CAST(N'2026-03-23T15:09:14.7662388+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-465e19bf', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:poi-bbq-night', CAST(N'2026-03-23T15:09:14.7036302+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-4d658113', N'Ánh Xuân', N'SYSTEM', N'Cập nhật POI', N'quang-truong-am-thuc-bbq-night', CAST(N'2026-03-23T15:09:14.6748460+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-de17906e', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:07:17.7228126+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-f5e84580', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:00:26.0584588+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2c6459c5', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T14:42:46.2012395+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-45963728', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-22T08:15:30.6439716+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2dd6d649', N'Minh Ánh', N'PLACE_OWNER', N'Đăng nhập admin', N'bbq@vinhkhanh.vn', CAST(N'2026-03-21T11:21:10.8821922+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2700d5bc', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật tài khoản admin', N'bbq@vinhkhanh.vn', CAST(N'2026-03-21T11:18:35.6725529+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-62561041', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T11:17:36.0739750+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-fc2031e1', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T11:08:06.6183748+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3ee8b781', N'Ánh Xuân', N'SYSTEM', N'Tạo nội dung thuyết minh', N'poi:zh-CN:poi-bbq-night', CAST(N'2026-03-21T03:10:22.6482714+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-58bd5af3', N'Ánh Xuân', N'SYSTEM', N'Tạo audio guide', N'poi:zh-CN:poi-bbq-night', CAST(N'2026-03-21T03:10:22.6043302+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-fcb65e4f', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T03:01:37.8873244+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-dd447361', N'Minh Ánh', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:poi-bbq-night', CAST(N'2026-03-21T02:26:09.7445932+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-a967b25e', N'Minh Ánh', N'SYSTEM', N'Cập nhật POI', N'quang-truong-am-thuc-bbq-night', CAST(N'2026-03-21T02:26:09.7190989+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-b259ec20', N'Minh Ánh', N'PLACE_OWNER', N'Cập nhật trạng thái đánh giá', N'review-1', CAST(N'2026-03-21T02:21:53.3383987+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2f58eea4', N'Minh Ánh', N'PLACE_OWNER', N'Cập nhật trạng thái đánh giá', N'review-1', CAST(N'2026-03-21T02:21:51.5003326+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-23a11658', N'Minh Ánh', N'PLACE_OWNER', N'Cập nhật trạng thái đánh giá', N'review-1', CAST(N'2026-03-21T02:21:49.9912229+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-44134089', N'Minh Ánh', N'PLACE_OWNER', N'Cập nhật trạng thái đánh giá', N'review-1', CAST(N'2026-03-21T02:21:49.1313145+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-a20b6180', N'Minh Ánh', N'PLACE_OWNER', N'Cập nhật trạng thái đánh giá', N'review-1', CAST(N'2026-03-21T02:21:47.0998968+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-10d614bd', N'Minh Ánh', N'PLACE_OWNER', N'Đăng nhập admin', N'bbq@vinhkhanh.vn', CAST(N'2026-03-21T02:19:14.7087424+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-e5f07e84', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:poi-bbq-night', CAST(N'2026-03-21T01:12:11.7750195+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-10db7490', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:poi-bbq-night', CAST(N'2026-03-21T01:12:11.7270186+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-55ebf34f', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T01:01:32.6315235+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-952ca80d', N'Ánh Xuân', N'SYSTEM', N'Cập nhật nội dung thuyết minh', N'poi:vi:poi-bbq-night', CAST(N'2026-03-21T00:53:15.4587231+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-43ec5465', N'Ánh Xuân', N'SYSTEM', N'Cập nhật audio guide', N'poi:vi:poi-bbq-night', CAST(N'2026-03-21T00:53:15.4148852+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-edc381f6', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T00:48:22.0356057+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-0042017d', N'Ánh Xuân', N'SUPER_ADMIN', N'Đăng nhập admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T00:03:59.7290246+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2', N'Minh Ánh', N'PLACE_OWNER', N'Cập nhật audio tiếng Anh cho BBQ Night', N'poi-bbq-night', CAST(N'2026-03-19T03:05:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-1', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật cấu hình ngôn ngữ hỗ trợ', N'settings', CAST(N'2026-03-19T02:15:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3', N'Lê Quốc Bảo', N'PLACE_OWNER', N'Cập nhật mô tả món ăn đặc trưng', N'poi-snail-signature', CAST(N'2026-03-18T07:12:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-4', N'Ánh Xuân', N'SUPER_ADMIN', N'Khóa tài khoản chủ quán Hẻm Chè', N'user-owner-dessert', CAST(N'2026-03-16T09:00:00.0000000+00:00' AS datetimeoffset(7)));
GO

INSERT INTO dbo.AdminAuditLogs (
    Id, ActorId, ActorName, ActorRole, ActorType, [Action], [Module], TargetId, TargetSummary,
    BeforeSummary, AfterSummary, SourceApp, LegacyAuditId, CreatedAt
)
SELECT
    legacy.Id,
    COALESCE(adminUser.Id, legacy.TargetValue, N'legacy-admin'),
    legacy.ActorName,
    legacy.ActorRole,
    N'ADMIN',
    legacy.[Action],
    CASE
        WHEN legacy.[Action] LIKE N'%đăng nhập%' OR legacy.[Action] LIKE N'%phiên đăng nhập%' THEN N'AUTH'
        WHEN legacy.[Action] LIKE N'%POI%' THEN N'POI'
        WHEN legacy.[Action] LIKE N'%audio%' THEN N'AUDIO_GUIDE'
        WHEN legacy.[Action] LIKE N'%thuyết minh%' THEN N'TRANSLATION'
        WHEN legacy.[Action] LIKE N'%tour%' THEN N'TOUR'
        WHEN legacy.[Action] LIKE N'%ưu đãi%' THEN N'PROMOTION'
        WHEN legacy.[Action] LIKE N'%đánh giá%' THEN N'REVIEW'
        WHEN legacy.[Action] LIKE N'%tài khoản admin%' OR legacy.[Action] LIKE N'%chủ quán%' THEN N'ADMIN_USER'
        WHEN legacy.[Action] LIKE N'%cài đặt%' OR legacy.[Action] LIKE N'%ngôn ngữ hỗ trợ%' THEN N'SETTINGS'
        ELSE N'LEGACY'
    END,
    legacy.TargetValue,
    legacy.TargetValue,
    NULL,
    NULL,
    N'ADMIN_WEB',
    legacy.Id,
    legacy.CreatedAt
FROM dbo.AuditLogs legacy
LEFT JOIN dbo.AdminUsers adminUser
    ON adminUser.Email = legacy.TargetValue
WHERE UPPER(COALESCE(legacy.ActorRole, N'')) IN (N'SUPER_ADMIN', N'PLACE_OWNER', N'SYSTEM')
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.AdminAuditLogs migrated
      WHERE migrated.LegacyAuditId = legacy.Id
  );

INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-105e45d4', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:01:11.5521958+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-46f8418f', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:02:40.0587692+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-5d8bee1d', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-owner-oc', N'quocbao@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:02:43.7760572+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-77ef1805', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-owner-oc', N'quocbao@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:03:46.0306875+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-4f4bd4ce', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-owner-bbq', N'minhanh@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:03:51.1904373+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-7b765d55', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-owner-bbq', N'minhanh@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:05:01.4532291+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-b51f0777', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:05:03.5128786+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-14438565', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:06:19.9907440+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-41578cd5', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-owner-bbq', N'minhanh@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:06:22.1563944+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-8b73aac8', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cập nhật POI chờ duyệt', N'POI', N'sushi-ko', N'nha-hang-sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:03.0399976+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-f77cbf41', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cap nhat noi dung thuyet minh', N'TRANSLATION', N'sushi-ko', N'poi:vi:sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:03.1090396+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-9e85892c', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cap nhat media asset', N'MEDIA', N'media-1', N'poi:sushi-ko:image', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:03.2555319+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-6652067b', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cap nhat mon an', N'FOOD_ITEM', N'food-2', N'Zaru Soba', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:03.6013206+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-9e2b91b3', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cap nhat audio guide', N'AUDIO_GUIDE', N'sushi-ko', N'poi:vi:sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:03.7853345+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-5bf4ff07', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-owner-bbq', N'minhanh@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:15.9921074+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-b5e7f638', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:17.5429845+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-3824c7c3', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Duyệt POI', N'POI', N'sushi-ko', N'nha-hang-sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:38.5294488+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-3572cd2e', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Cap nhat noi dung thuyet minh', N'TRANSLATION', N'sushi-ko', N'poi:vi:sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:11:38.5658313+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-e6e3c365', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:15:25.8126161+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-be435d37', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-owner-bbq', N'minhanh@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:15:27.8489972+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-92c24a40', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cập nhật POI chờ duyệt', N'POI', N'sushi-ko', N'nha-hang-sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:17:56.0590836+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-424c9b95', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cap nhat noi dung thuyet minh', N'TRANSLATION', N'sushi-ko', N'poi:vi:sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:17:56.1046352+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-379494b9', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Tao mon an', N'FOOD_ITEM', N'food-5f850dc5', N'Kajiki Maguro Yaki', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:17:56.2370105+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-6ba89de5', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Cap nhat audio guide', N'AUDIO_GUIDE', N'sushi-ko', N'poi:vi:sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:17:56.3918474+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-e2dcdff4', N'user-owner-bbq', N'Minh Ánh', N'PLACE_OWNER', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-owner-bbq', N'minhanh@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:18:22.9914753+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-cfdead52', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:18:24.4864718+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-e164ee92', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Duyệt POI', N'POI', N'sushi-ko', N'nha-hang-sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:18:37.8218590+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-4dfeb78f', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Cap nhat noi dung thuyet minh', N'TRANSLATION', N'sushi-ko', N'poi:vi:sushi-ko', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:18:37.8628870+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-5dfadf1b', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:19:43.9866723+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-92832225', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-owner-oc', N'quocbao@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:19:46.3134310+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-09bbf903', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Cập nhật POI chờ duyệt', N'POI', N'oc-phat', N'oc-phat', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:11.4184099+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-2edddf47', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Cap nhat noi dung thuyet minh', N'TRANSLATION', N'oc-phat', N'poi:vi:oc-phat', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:11.4672458+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-63f62c0a', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Cap nhat media asset', N'MEDIA', N'media-2', N'poi:oc-phat:image', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:11.5967357+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-f00dce8b', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Cap nhat mon an', N'FOOD_ITEM', N'food-1', N'Ốc móng tay xào/bơ/tỏi/me', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:11.8572693+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-6af92d66', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Tao mon an', N'FOOD_ITEM', N'food-b61e8e41', N'Ốc giác xào rau muống', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:12.0015490+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-412cbf06', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Cap nhat audio guide', N'AUDIO_GUIDE', N'oc-phat', N'poi:vi:oc-phat', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:12.0692432+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-2ab74aa4', N'user-owner-oc', N'Lê Quốc Bảo', N'PLACE_OWNER', N'ADMIN', N'Đăng xuất admin', N'AUTH', N'user-owner-oc', N'quocbao@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:13.9389122+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-05077347', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Đăng nhập admin', N'AUTH', N'user-super', N'anhxuan@gmail.com', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:15.4781609+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-67964f64', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Duyệt POI', N'POI', N'oc-phat', N'oc-phat', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:22.3202114+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AdminAuditLogs ([Id], [ActorId], [ActorName], [ActorRole], [ActorType], [Action], [Module], [TargetId], [TargetSummary], [BeforeSummary], [AfterSummary], [SourceApp], [LegacyAuditId], [CreatedAt]) VALUES (N'audit-290df68b', N'user-super', N'Ánh Xuân', N'SUPER_ADMIN', N'ADMIN', N'Cap nhat noi dung thuyet minh', N'TRANSLATION', N'oc-phat', N'poi:vi:oc-phat', NULL, NULL, N'ADMIN_WEB', NULL, CAST(N'2026-04-09T16:25:22.3600013+00:00' AS datetimeoffset(7)));
GO

INSERT INTO dbo.RefreshSessions (AccessToken, RefreshToken, UserId, AccessTokenExpiresAt, ExpiresAt) VALUES (N'vk_access_8a51956446414a53b3b7a46755a65894', N'vk_refresh_c651fd66494841beb09044011ec3d862', N'user-super', CAST(N'2026-04-10T00:25:15.4876735+00:00' AS datetimeoffset(7)), CAST(N'2026-05-09T16:25:15.4876735+00:00' AS datetimeoffset(7)));
GO

INSERT INTO dbo.SystemSettings (Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage, PremiumUnlockPriceUsd, MapProvider, StorageProvider, TtsProvider, GeofenceRadiusMeters, GuestReviewEnabled, AnalyticsRetentionDays) VALUES (1, N'Hệ thống quản trị thuyết minh Vĩnh Khánh', N'support@vinhkhanh.vn', N'vi', N'en', 0, N'openstreetmap', N'cloudinary', N'elevenlabs', 60, 1, 180);
GO
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'supported', N'en');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'supported', N'ja');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'supported', N'ko');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'supported', N'vi');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'supported', N'zh-CN');
GO

