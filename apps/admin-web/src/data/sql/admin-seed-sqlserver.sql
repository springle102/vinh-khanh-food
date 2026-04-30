IF DB_ID(N'VinhKhanhFoodAdmin') IS NULL
BEGIN
    EXEC(N'CREATE DATABASE [VinhKhanhFoodAdmin]');
END;
GO

USE [VinhKhanhFoodAdmin];
GO

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
IF OBJECT_ID(N'dbo.AppPresence', N'U') IS NOT NULL DROP TABLE dbo.AppPresence;
IF OBJECT_ID(N'dbo.AppUsageEvents', N'U') IS NOT NULL DROP TABLE dbo.AppUsageEvents;
IF OBJECT_ID(N'dbo.Reviews', N'U') IS NOT NULL DROP TABLE dbo.Reviews;
IF OBJECT_ID(N'dbo.PoiChangeRequests', N'U') IS NOT NULL DROP TABLE dbo.PoiChangeRequests;
IF OBJECT_ID(N'dbo.Promotions', N'U') IS NOT NULL DROP TABLE dbo.Promotions;
IF OBJECT_ID(N'dbo.MediaAssets', N'U') IS NOT NULL DROP TABLE dbo.MediaAssets;
IF OBJECT_ID(N'dbo.AudioGuides', N'U') IS NOT NULL DROP TABLE dbo.AudioGuides;
IF OBJECT_ID(N'dbo.PlaceTranslations', N'U') IS NOT NULL DROP TABLE dbo.PlaceTranslations;
IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL DROP TABLE dbo.PoiTranslations;
IF OBJECT_ID(N'dbo.FoodItems', N'U') IS NOT NULL DROP TABLE dbo.FoodItems;
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
    PlaceTier INT NOT NULL CONSTRAINT DF_Pois_PlaceTier DEFAULT (0),
    OwnerUserId NVARCHAR(50) NULL,
    UpdatedBy NVARCHAR(120) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Pois_IsActive DEFAULT (1),
    LockedBySuperAdmin BIT NOT NULL CONSTRAINT DF_Pois_LockedBySuperAdmin DEFAULT (0),
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
    [Status] NVARCHAR(20) NOT NULL,
    VisibleFrom DATETIMEOFFSET(7) NULL,
    CreatedByUserId NVARCHAR(50) NOT NULL,
    OwnerUserId NVARCHAR(50) NULL,
    IsDeleted BIT NOT NULL CONSTRAINT DF_Promotions_IsDeleted DEFAULT (0),
    CONSTRAINT FK_Promotions_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE TABLE dbo.PoiChangeRequests (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PoiId NVARCHAR(50) NOT NULL,
    SubmittedByUserId NVARCHAR(50) NOT NULL,
    SubmittedByName NVARCHAR(120) NOT NULL,
    BeforeJson NVARCHAR(MAX) NOT NULL,
    AfterJson NVARCHAR(MAX) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    RejectionReason NVARCHAR(1000) NULL,
    SubmittedAt DATETIMEOFFSET(7) NOT NULL,
    ReviewedAt DATETIMEOFFSET(7) NULL,
    ReviewedByUserId NVARCHAR(50) NULL,
    ReviewedByName NVARCHAR(120) NULL,
    AppliedAt DATETIMEOFFSET(7) NULL,
    CONSTRAINT FK_PoiChangeRequests_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id),
    CONSTRAINT FK_PoiChangeRequests_SubmittedBy FOREIGN KEY (SubmittedByUserId) REFERENCES dbo.AdminUsers(Id)
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
    IdempotencyKey NVARCHAR(100) NULL,
    CONSTRAINT FK_AppUsageEvents_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE INDEX IX_AppUsageEvents_OccurredAt
ON dbo.AppUsageEvents (OccurredAt DESC, EventType, PoiId, LanguageCode);
GO

CREATE UNIQUE INDEX UX_AppUsageEvents_IdempotencyKey
ON dbo.AppUsageEvents (IdempotencyKey)
WHERE IdempotencyKey IS NOT NULL AND IdempotencyKey <> N'';
GO

CREATE TABLE dbo.AppPresence (
    Id NVARCHAR(60) NOT NULL PRIMARY KEY,
    ClientId NVARCHAR(80) NOT NULL,
    LastSeenAtUtc DATETIMEOFFSET(7) NOT NULL,
    CreatedAtUtc DATETIMEOFFSET(7) NOT NULL,
    Platform NVARCHAR(20) NOT NULL,
    AppVersion NVARCHAR(40) NOT NULL
);
GO

CREATE UNIQUE INDEX UX_AppPresence_ClientId
ON dbo.AppPresence (ClientId);
GO

CREATE INDEX IX_AppPresence_LastSeenAtUtc
ON dbo.AppPresence (LastSeenAtUtc DESC);
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

CREATE TABLE dbo.UserActivityLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    ActorId NVARCHAR(50) NOT NULL,
    ActorType NVARCHAR(30) NOT NULL,
    EventType NVARCHAR(160) NOT NULL,
    Metadata NVARCHAR(MAX) NOT NULL,
    SourceApp NVARCHAR(60) NOT NULL,
    LegacyAuditId NVARCHAR(50) NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL
);
GO

CREATE UNIQUE INDEX UX_UserActivityLogs_LegacyAuditId
ON dbo.UserActivityLogs (LegacyAuditId)
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

CREATE UNIQUE INDEX UX_RefreshSessions_AccessToken ON dbo.RefreshSessions (AccessToken);
GO

CREATE TABLE dbo.SystemSettings (
    Id INT NOT NULL PRIMARY KEY,
    AppName NVARCHAR(200) NOT NULL,
    SupportEmail NVARCHAR(200) NOT NULL,
    SupportPhone NVARCHAR(50) NOT NULL,
    ContactAddress NVARCHAR(300) NOT NULL,
    SupportInstructions NVARCHAR(2000) NOT NULL,
    SupportHours NVARCHAR(120) NOT NULL,
    ContactUpdatedAtUtc DATETIMEOFFSET(7) NOT NULL,
    DefaultLanguage NVARCHAR(20) NOT NULL,
    FallbackLanguage NVARCHAR(20) NOT NULL,
    StorageProvider NVARCHAR(50) NOT NULL,
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
(N'cat-bbq', N'Nướng & Beer', N'nuong-beer', N'flame', N'#de6245'),
(N'cat-coffee-tea', N'Cà phê & tráng miệng', N'ca-phe-trang-mieng', N'coffee', N'#8b5e3c'),
(N'cat-oc', N'Ốc & Hải sản', N'oc-hai-san', N'shell', N'#f97316'),
(N'cat-seafood-raw', N'Hải sản & món Nhật', N'hai-san-mon-nhat', N'fish', N'#0f766e');
GO

INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES
(N'user-faf2da21', N'Vương Thanh', N'thanh@gmail.com', N'0325417896', N'PLACE_OWNER', N'thanh@123', N'active', '2026-04-20T08:17:47.7851657+00:00', '2026-04-20T09:38:34.3231193+00:00', N'#f97316', NULL, N'approved', NULL, '2026-04-20T08:17:47.7851657+00:00', '2026-04-20T08:18:08.2780498+00:00'),
(N'user-owner-dessert', N'Phạm Mỹ Linh', N'mylinh@gmail.com', N'0909 188 004', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-22T16:37:10.5277413+00:00', N'#8b5e3c', N'ca-phe-che', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-owner-oanh', N'Tiểu Mỹ', N'tieumy123@gmail.com', N'0952 478 925', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-22T16:37:19.8658900+00:00', N'#d97706', N'oc-oanh-1', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-owner-oc', N'Lê Quốc Bảo', N'quocbao@gmail.com', N'0909 188 003', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-22T16:37:02.4904557+00:00', N'#f97316', N'oc-phat', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-owner-sushi', N'Minh Ánh', N'minhanh@gmail.com', N'0909 188 002', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-22T16:36:53.6771822+00:00', N'#0f766e', N'sushi-ko', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-super', N'Ánh Xuân', N'anhxuan@gmail.com', N'0909 188 001', N'SUPER_ADMIN', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-22T16:37:54.9938756+00:00', N'#f97316', NULL, N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00');
GO

INSERT INTO dbo.Pois (Id, Slug, Title, ShortDescription, [Description], AudioScript, SourceLanguageCode, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, TriggerRadius, Priority, PlaceTier, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt, IsActive, LockedBySuperAdmin, ApprovedAt, RejectionReason, RejectedAt) VALUES
(N'ca-phe-che', N'ca-phe-che', N'Cà phê Chè', N'Quán tráng miệng và cà phê nhẹ nhàng, phù hợp nghỉ chân sau khi ăn hải sản.', N'Chào mừng bạn đến với Cà phê Chè. Sau những món ốc và hải sản đậm vị, đây là nơi phù hợp để nghỉ chân bằng một ly cà phê hoặc chén chè mát. Không gian nhẹ nhàng, dễ ngồi trò chuyện và rất hợp để kết thúc hành trình ẩm thực Vĩnh Khánh bằng một vị ngọt dễ chịu.', N'Chào mừng bạn đến với Cà phê Chè. Sau những món ốc và hải sản đậm vị, đây là nơi phù hợp để nghỉ chân bằng một ly cà phê hoặc chén chè mát. Không gian nhẹ nhàng, dễ ngồi trò chuyện và rất hợp để kết thúc hành trình ẩm thực Vĩnh Khánh bằng một vị ngọt dễ chịu.', N'vi', N'Chung cư H1, Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM', 10.762195, 106.701499, N'cat-coffee-tea', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'25.000 - 75.000 VND', 20, 2, 0, N'user-owner-dessert', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-18T11:45:45.7649279+00:00', 1, 0, '2026-04-18T11:45:45.7629361+00:00', NULL, NULL),
(N'oc-loan', N'nha-hang-oc-loan', N'Nhà hàng Ốc Loan', N'', N'Điểm cộng lớn nhất của Ốc Loan là hải sản luôn tươi rói, các loại ốc to, mập mạp và được nêm nếm rất đậm đà, thiên một chút xíu về khẩu vị ngọt đặc trưng của miền Nam. Nếu có dịp ghé quán, bạn nhất định nên gọi thử những món quen thuộc nhưng cực kỳ "bắt miệng" như: sò huyết xào me chua ngọt sền sệt, ốc hương xào bơ tỏi thơm lừng chấm kèm bánh mì, hay một phần sụn gà chiên nước mắm giòn rụm để nhâm nhi.', N'Điểm cộng lớn nhất của Ốc Loan là hải sản luôn tươi rói, các loại ốc to, mập mạp và được nêm nếm rất đậm đà, thiên một chút xíu về khẩu vị ngọt đặc trưng của miền Nam. Nếu có dịp ghé quán, bạn nhất định nên gọi thử những món quen thuộc nhưng cực kỳ "bắt miệng" như: sò huyết xào me chua ngọt sền sệt, ốc hương xào bơ tỏi thơm lừng chấm kèm bánh mì, hay một phần sụn gà chiên nước mắm giòn rụm để nhâm nhi.', N'vi', N'Nhà Hàng Ốc Loan, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.761159, 106.702695, N'cat-oc', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'50.000-150.000', 25, 4, 0, N'user-faf2da21', N'Ánh Xuân', '2026-04-20T08:25:46.0942429+00:00', '2026-04-20T09:38:59.4688044+00:00', 1, 0, '2026-04-20T09:19:25.8639560+00:00', NULL, NULL),
(N'oc-oanh-1', N'oc-oanh-1', N'Ốc Oanh 1', N'', N'Bạn đang dừng chân tại Ốc Oanh 1, một tọa độ hải sản quen thuộc trên tuyến Vĩnh Khánh. Quầy ốc phía trước giúp bạn nhìn thấy nguyên liệu trước khi gọi món. Nếu thích vị chua ngọt, hãy chọn sò huyết rang me. Nếu thích mùi thơm béo, sò điệp nướng mỡ hành là lựa chọn rất dễ nhớ.', N'Bạn đang dừng chân tại Ốc Oanh 1, một tọa độ hải sản quen thuộc trên tuyến Vĩnh Khánh. Quầy ốc phía trước giúp bạn nhìn thấy nguyên liệu trước khi gọi món. Nếu thích vị chua ngọt, hãy chọn sò huyết rang me. Nếu thích mùi thơm béo, sò điệp nướng mỡ hành là lựa chọn rất dễ nhớ.', N'vi', N'534 hẻm 42/237 Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM', 10.761415, 106.702834, N'cat-oc', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'40.000 - 120.000 VND', 25, 3, 1, N'user-owner-oanh', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-22T16:38:06.8020980+00:00', 1, 0, '2026-04-22T16:38:06.7825995+00:00', NULL, NULL),
(N'oc-phat', N'oc-phat', N'Ốc Phát', N'Quán ốc đầu phố với thực đơn hải sản đa dạng, phù hợp nhóm bạn và khách lần đầu đến Vĩnh Khánh.', N'Bạn đang ở Ốc Phát, một trong những điểm dừng dễ tiếp cận nhất khi bắt đầu khám phá phố ẩm thực Vĩnh Khánh. Hãy thử các món ốc hương rang muối, nghêu hấp Thái hoặc ốc móng tay xào bơ tỏi. Điểm hấp dẫn của quán là không khí tụ tập rộn ràng, món lên nhanh và hương hải sản lan rất rõ trong buổi tối Quận 4.', N'Bạn đang ở Ốc Phát, một trong những điểm dừng dễ tiếp cận nhất khi bắt đầu khám phá phố ẩm thực Vĩnh Khánh. Hãy thử các món ốc hương rang muối, nghêu hấp Thái hoặc ốc móng tay xào bơ tỏi. Điểm hấp dẫn của quán là không khí tụ tập rộn ràng, món lên nhanh và hương hải sản lan rất rõ trong buổi tối Quận 4.', N'vi', N'Ốc Phát, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.761902, 106.702158, N'cat-oc', N'published', 1, N'Quận 4', N'Phường Khánh Hội', N'50.000 - 110.000 VND', 30, 5, 0, N'user-owner-oc', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-20T08:50:15.2450584+00:00', 1, 0, '2026-04-18T11:35:31.3429292+00:00', NULL, NULL),
(N'sushi-ko', N'nha-hang-sushi-ko', N'Nhà Hàng Sushi Ko', N'Điểm dừng món Nhật gần phố ẩm thực Vĩnh Khánh, nổi bật với sushi và hải sản chế biến tươi.', N'Chào mừng bạn đến với Nhà Hàng Sushi Ko. Đây là điểm dừng dành cho những ai yêu thích hương vị Nhật Bản giữa khu phố ẩm thực Vĩnh Khánh. Quán nổi bật với sushi, mì soba lạnh và các món cá nướng được chuẩn bị kỹ lưỡng. Hãy dành ít phút cảm nhận sự đối lập thú vị giữa nhịp phố sôi động bên ngoài và phong cách ẩm thực tinh tế bên trong.', N'Chào mừng bạn đến với Nhà Hàng Sushi Ko. Đây là điểm dừng dành cho những ai yêu thích hương vị Nhật Bản giữa khu phố ẩm thực Vĩnh Khánh. Quán nổi bật với sushi, mì soba lạnh và các món cá nướng được chuẩn bị kỹ lưỡng. Hãy dành ít phút cảm nhận sự đối lập thú vị giữa nhịp phố sôi động bên ngoài và phong cách ẩm thực tinh tế bên trong.', N'vi', N'122/37/15 Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM', 10.760772, 106.704798, N'cat-seafood-raw', N'published', 1, N'Quận 4', N'Phường Khánh Hội', N'50.000 - 150.000 VND', 25, 4, 0, N'user-owner-sushi', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-18T11:35:24.0691280+00:00', 1, 0, '2026-04-18T11:35:24.0661212+00:00', NULL, NULL);
GO

INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, TranscriptText, AudioFilePath, AudioFileName, VoiceType, SourceType, Provider, VoiceId, ModelId, OutputFormat, DurationInSeconds, FileSizeBytes, TextHash, ContentVersion, GeneratedAt, GenerationStatus, ErrorMessage, IsOutdated, [Status], UpdatedBy, UpdatedAt) VALUES
(N'audio-d3cc4b51', N'poi', N'ca-phe-che', N'en', N'/storage/audio/pois/ca-phe-che/en/ca-phe-che-en-00cd880d9843-20260424132450097.mp3', N'Welcome to Tea Coffee. After delicious snail and seafood dishes, this is the right place to rest with a cool cup of coffee or tea. The space is gentle, easy to sit and chat and is very suitable to end the Vinh Khanh culinary journey with a pleasant sweetness.', N'storage/audio/pois/ca-phe-che/en/ca-phe-che-en-00cd880d9843-20260424132450097.mp3', N'ca-phe-che-en-00cd880d9843-20260424132450097.mp3', N'standard', N'generated', N'elevenlabs', N'EXAVITQu4vr4xnSDxMaL', N'eleven_flash_v2_5', N'mp3_44100_128', 17.2958125, 252073, N'00cd880d98437d4474db4cfb40509a834b4f80ed80560c554c0d7f49a2c591cd', N'00cd880d98437d4474db4cfb40509a834b4f80ed80560c554c0d7f49a2c591cd', '2026-04-18T11:41:15.512+00:00', N'success', N'', 0, N'ready', N'Phạm Mỹ Linh', '2026-04-18T11:41:15.512+00:00'),
(N'audio-bf224495', N'poi', N'ca-phe-che', N'ja', N'/storage/audio/pois/ca-phe-che/ja/ca-phe-che-ja-33e14a759899-20260424132453482.mp3', N'ティーコーヒーへようこそ。おいしいカタツムリやシーフード料理を楽しんだ後は、冷たいコーヒーや紅茶を飲みながら休憩するのに最適な場所です。穏やかで座っておしゃべりしやすい空間は、ヴィン・カイン料理の旅を心地よい甘味で締めくくるのに最適です。', N'storage/audio/pois/ca-phe-che/ja/ca-phe-che-ja-33e14a759899-20260424132453482.mp3', N'ca-phe-che-ja-33e14a759899-20260424132453482.mp3', N'standard', N'generated', N'elevenlabs', N'XrExE9yKIg1WjnnlVkGX', N'eleven_flash_v2_5', N'mp3_44100_128', 19.2549375, 310169, N'33e14a759899e60a76770ef1e9dd48de596f30c9fa5a512e138696365969b6d0', N'33e14a759899e60a76770ef1e9dd48de596f30c9fa5a512e138696365969b6d0', '2026-04-18T11:41:17.724+00:00', N'success', N'', 0, N'ready', N'Phạm Mỹ Linh', '2026-04-18T11:41:17.724+00:00'),
(N'audio-e73ed181', N'poi', N'ca-phe-che', N'ko', N'/storage/audio/pois/ca-phe-che/ko/ca-phe-che-ko-a7d587415819-20260424132446921.mp3', N'티커피에 오신 것을 환영합니다. 맛있는 달팽이 요리와 해산물 요리를 즐긴 후 시원한 커피나 차 한 잔과 함께 휴식을 취하기에 딱 좋은 곳입니다. 공간은 부드럽고 앉아서 이야기를 나누기 쉬우며 기분 좋은 달콤함과 함께 Vinh Khanh 요리 여행을 마무리하기에 매우 적합합니다.', N'storage/audio/pois/ca-phe-che/ko/ca-phe-che-ko-a7d587415819-20260424132446921.mp3', N'ca-phe-che-ko-a7d587415819-20260424132446921.mp3', N'standard', N'generated', N'elevenlabs', N'onwK4e9ZLuTAKqWW03F9', N'eleven_flash_v2_5', N'mp3_44100_128', 23.930875, 377879, N'a7d58741581921d524b69380b663054d5f94934fb462045061ab33ee74a4e76c', N'a7d58741581921d524b69380b663054d5f94934fb462045061ab33ee74a4e76c', '2026-04-18T11:41:20.314+00:00', N'success', N'', 0, N'ready', N'Phạm Mỹ Linh', '2026-04-18T11:41:20.314+00:00'),
(N'audio-759baccb', N'poi', N'ca-phe-che', N'vi', N'/storage/audio/pois/ca-phe-che/vi/ca-phe-che-vi-a992769838c6-20260424132456384.mp3', N'Chào mừng bạn đến với Cà phê Chè. Sau những món ốc và hải sản đậm vị, đây là nơi phù hợp để nghỉ chân bằng một ly cà phê hoặc chén chè mát. Không gian nhẹ nhàng, dễ ngồi trò chuyện và rất hợp để kết thúc hành trình ẩm thực Vĩnh Khánh bằng một vị ngọt dễ chịu.', N'storage/audio/pois/ca-phe-che/vi/ca-phe-che-vi-a992769838c6-20260424132456384.mp3', N'ca-phe-che-vi-a992769838c6-20260424132456384.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 14.4223125, 234519, N'a992769838c6312dfe9a42f08e42ecc20103a4b2f24a05de08600fa11dcbaf4e', N'a992769838c6312dfe9a42f08e42ecc20103a4b2f24a05de08600fa11dcbaf4e', '2026-04-18T11:41:08.594+00:00', N'success', N'', 0, N'ready', N'Phạm Mỹ Linh', '2026-04-18T11:41:08.594+00:00'),
(N'audio-15832854', N'poi', N'ca-phe-che', N'zh-CN', N'/storage/audio/pois/ca-phe-che/zh-CN/ca-phe-che-zh-CN-8f3e4cb7850e-20260424132459615.mp3', N'欢迎来到茶咖啡。享用完美味的蜗牛和海鲜菜肴后，这里是您享用一杯清凉的咖啡或茶休息的好地方。空间柔和，很容易坐下来聊天，非常适合以愉快的甜蜜结束Vinh Khanh的美食之旅。', N'storage/audio/pois/ca-phe-che/zh-CN/ca-phe-che-zh-CN-8f3e4cb7850e-20260424132459615.mp3', N'ca-phe-che-zh-CN-8f3e4cb7850e-20260424132459615.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 17.40025, 281330, N'8f3e4cb7850edd1877e7e499abf40ca633cb24fdcc3416acb89b26cae9afaf6a', N'8f3e4cb7850edd1877e7e499abf40ca633cb24fdcc3416acb89b26cae9afaf6a', '2026-04-18T11:41:22.226+00:00', N'success', N'', 0, N'ready', N'Phạm Mỹ Linh', '2026-04-18T11:41:22.226+00:00'),
(N'audio-a1d402e3', N'poi', N'oc-loan', N'en', N'/storage/audio/pois/oc-loan/en/oc-loan-en-41dd75a8111f-20260424132432155.mp3', N'The biggest plus point of Oc Loan is that the seafood is always fresh, the snails are big, plump and very richly seasoned, with a slight bias towards the typical sweet taste of the South. If you have the opportunity to visit the restaurant, you should definitely try familiar but extremely "mouth-watering" dishes such as: blood cockles stir-fried with sweet and sour tamarind, snails stir-fried with fragrant garlic butter and dipped in bread, or a portion of fried chicken cartilage with crispy fish sauce to enjoy.', N'storage/audio/pois/oc-loan/en/oc-loan-en-41dd75a8111f-20260424132432155.mp3', N'oc-loan-en-41dd75a8111f-20260424132432155.mp3', N'standard', N'generated', N'elevenlabs', N'EXAVITQu4vr4xnSDxMaL', N'eleven_flash_v2_5', N'mp3_44100_128', 32.2116875, 495325, N'41dd75a8111ff619913218768bd86c428682809aafce3940d0823007bca5a81f', N'41dd75a8111ff619913218768bd86c428682809aafce3940d0823007bca5a81f', '2026-04-20T09:38:51.118+00:00', N'success', N'', 0, N'ready', N'Vương Thanh', '2026-04-20T09:38:51.118+00:00'),
(N'audio-a68d21c3', N'poi', N'oc-loan', N'ja', N'/storage/audio/pois/oc-loan/ja/oc-loan-ja-4ffe9dc4d31c-20260424132428520.mp3', N'Oc Loan の最大の利点は、シーフードが常に新鮮であること、カタツムリは大きくてぷりぷりしていて、南部の典型的な甘い味に少し偏りがある非常に濃厚な味付けであることです。アカザルの甘酸っぱいタマリンド炒め、カタツムリの香りガーリックバター炒めをパンにつけて食べる、鶏軟骨の唐揚げをカリカリ魚醤で食べるなど、馴染み深いながらも「食欲をそそる」料理を、もしお店を訪れる機会があればぜひ味わってみてください。', N'storage/audio/pois/oc-loan/ja/oc-loan-ja-4ffe9dc4d31c-20260424132428520.mp3', N'oc-loan-ja-4ffe9dc4d31c-20260424132428520.mp3', N'standard', N'generated', N'elevenlabs', N'XrExE9yKIg1WjnnlVkGX', N'eleven_flash_v2_5', N'mp3_44100_128', 34.56275, 562199, N'4ffe9dc4d31ca210d5257314614d21c625922445dcdc8db3bd5e776a75da343b', N'4ffe9dc4d31ca210d5257314614d21c625922445dcdc8db3bd5e776a75da343b', '2026-04-20T09:38:53.627+00:00', N'success', N'', 0, N'ready', N'Vương Thanh', '2026-04-20T09:38:53.627+00:00'),
(N'audio-5e092e53', N'poi', N'oc-loan', N'ko', N'/storage/audio/pois/oc-loan/ko/oc-loan-ko-4363f9a03f76-20260424132439686.mp3', N'Oc Loan의 가장 큰 장점은 해산물이 항상 신선하고 달팽이가 크고 통통하며 양념이 풍부하고 전형적인 남부의 달콤한 맛에 약간의 편견이 있다는 것입니다. 레스토랑을 방문할 기회가 있다면 달콤하고 신맛이 나는 타마린드와 함께 볶은 꼬막 요리, 향긋한 마늘 버터와 함께 볶은 빵에 담근 달팽이 요리, 바삭한 생선 소스를 곁들인 닭 연골 튀김 등 친숙하면서도 군침이 도는 요리를 꼭 맛보세요.', N'storage/audio/pois/oc-loan/ko/oc-loan-ko-4363f9a03f76-20260424132439686.mp3', N'oc-loan-ko-4363f9a03f76-20260424132439686.mp3', N'standard', N'generated', N'elevenlabs', N'onwK4e9ZLuTAKqWW03F9', N'eleven_flash_v2_5', N'mp3_44100_128', 36.626375, 596889, N'4363f9a03f76fbb1a82f91e063d5b5009a458ccd1f8f35ac47b20eca9a13e1f1', N'4363f9a03f76fbb1a82f91e063d5b5009a458ccd1f8f35ac47b20eca9a13e1f1', '2026-04-20T09:38:56.528+00:00', N'success', N'', 0, N'ready', N'Vương Thanh', '2026-04-20T09:38:56.528+00:00'),
(N'audio-199b8949', N'poi', N'oc-loan', N'vi', N'/storage/audio/pois/oc-loan/vi/oc-loan-vi-12e1d8405d03-20260424132442872.mp3', N'Điểm cộng lớn nhất của Ốc Loan là hải sản luôn tươi rói, các loại ốc to, mập mạp và được nêm nếm rất đậm đà, thiên một chút xíu về khẩu vị ngọt đặc trưng của miền Nam. Nếu có dịp ghé quán, bạn nhất định nên gọi thử những món quen thuộc nhưng cực kỳ "bắt miệng" như: sò huyết xào me chua ngọt sền sệt, ốc hương xào bơ tỏi thơm lừng chấm kèm bánh mì, hay một phần sụn gà chiên nước mắm giòn rụm để nhâm nhi.', N'storage/audio/pois/oc-loan/vi/oc-loan-vi-12e1d8405d03-20260424132442872.mp3', N'oc-loan-vi-12e1d8405d03-20260424132442872.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 24.6361875, 384566, N'12e1d8405d03e7bbe62763cb0ff0a9dfc0ad8695ab4dc69ded9a82801aafee40', N'12e1d8405d03e7bbe62763cb0ff0a9dfc0ad8695ab4dc69ded9a82801aafee40', '2026-04-20T09:17:41.206+00:00', N'success', N'', 0, N'ready', N'Vương Thanh', '2026-04-20T09:17:41.206+00:00'),
(N'audio-722efdff', N'poi', N'oc-loan', N'zh-CN', N'/storage/audio/pois/oc-loan/zh-CN/oc-loan-zh-CN-5cc1c9c87e3b-20260424132435899.mp3', N'Oc Loan最大的优点就是海鲜总是新鲜，螺又大又肥，调味也很丰富，稍微偏向南方典型的甜味。如果你有机会来到这家餐厅，你一定要尝尝那些熟悉却又极其“令人垂涎”的菜肴，比如：酸甜罗望子炒血蚶、蒜蓉黄油炒蜗牛沾面包，或者炸鸡软骨配脆皮鱼露享用。', N'storage/audio/pois/oc-loan/zh-CN/oc-loan-zh-CN-5cc1c9c87e3b-20260424132435899.mp3', N'oc-loan-zh-CN-5cc1c9c87e3b-20260424132435899.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 24.9496875, 415495, N'5cc1c9c87e3bb10346d73ea7544b2cef6b3d1716ceaf0d494c69a7fc72a61393', N'5cc1c9c87e3bb10346d73ea7544b2cef6b3d1716ceaf0d494c69a7fc72a61393', '2026-04-20T09:38:59.460+00:00', N'success', N'', 0, N'ready', N'Vương Thanh', '2026-04-20T09:38:59.460+00:00'),
(N'audio-ce388e4f', N'poi', N'oc-oanh-1', N'en', N'/storage/audio/pois/oc-oanh-1/en/oc-oanh-1-en-273b37553526-20260424125846535.mp3', N'You are stopping at Oc Oanh 1, a familiar seafood coordinates on the Vinh Khanh route. The snail counter in front helps you see the ingredients before ordering. If you like sweet and sour taste, choose roasted cockles with tamarind. If you like the fatty aroma, grilled scallops with onion fat is a very memorable choice.', N'storage/audio/pois/oc-oanh-1/en/oc-oanh-1-en-273b37553526-20260424125846535.mp3', N'oc-oanh-1-en-273b37553526-20260424125846535.mp3', N'standard', N'generated', N'elevenlabs', N'EXAVITQu4vr4xnSDxMaL', N'eleven_flash_v2_5', N'mp3_44100_128', 19.908, 325634, N'273b375535262ad5012b6547a3c89ed4c222764e0571d825ec179585d7f1b2b4', N'273b375535262ad5012b6547a3c89ed4c222764e0571d825ec179585d7f1b2b4', '2026-04-18T11:43:51.684+00:00', N'success', N'', 0, N'ready', N'Tiểu Mỹ', '2026-04-18T11:43:51.684+00:00'),
(N'audio-dd33ab65', N'poi', N'oc-oanh-1', N'ja', N'/storage/audio/pois/oc-oanh-1/ja/oc-oanh-1-ja-af1b9f1794e3-20260424132355764.mp3', N'ヴィン カイン ルートでおなじみのシーフード コーディネートである Oc Oanh 1 に立ち寄ります。正面のカタツムリカウンターは、注文する前に材料を確認するのに役立ちます。甘酸っぱい味が好きなら、タマリンド入りのローストザルガイを選んでください。脂の香りが好きなら、玉ねぎの脂を添えたホタテのグリルはとても思い出に残る一品です。', N'storage/audio/pois/oc-oanh-1/ja/oc-oanh-1-ja-af1b9f1794e3-20260424132355764.mp3', N'oc-oanh-1-ja-af1b9f1794e3-20260424132355764.mp3', N'standard', N'generated', N'elevenlabs', N'XrExE9yKIg1WjnnlVkGX', N'eleven_flash_v2_5', N'mp3_44100_128', 28.05825, 437229, N'e4ff22bfc1ae', N'e4ff22bfc1ae', '2026-04-18T11:43:51.737+00:00', N'success', N'', 0, N'ready', N'Tiểu Mỹ', '2026-04-18T11:43:51.737+00:00'),
(N'audio-6850ca51', N'poi', N'oc-oanh-1', N'ko', N'/storage/audio/pois/oc-oanh-1/ko/oc-oanh-1-ko-5d1f65132f57-20260424132405752.mp3', N'Vinh Khanh 노선의 친숙한 해산물 좌표인 Oc Oanh 1에 정차합니다. 앞에 달팽이 카운터가 있어서 주문하기 전에 재료를 확인할 수 있어요. 새콤달콤한 맛을 좋아한다면 타마린드를 곁들인 꼬막구이를 선택해보세요. 기름진 향을 좋아한다면 양파 지방을 곁들인 가리비 구이는 매우 기억에 남는 선택입니다.', N'storage/audio/pois/oc-oanh-1/ko/oc-oanh-1-ko-5d1f65132f57-20260424132405752.mp3', N'oc-oanh-1-ko-5d1f65132f57-20260424132405752.mp3', N'standard', N'generated', N'elevenlabs', N'onwK4e9ZLuTAKqWW03F9', N'eleven_flash_v2_5', N'mp3_44100_128', 25.7855625, 443916, N'5d1f65132f57aae939b39a972c9feaa550f9a04aca2a0102d6c5babd09d3ecfb', N'5d1f65132f57aae939b39a972c9feaa550f9a04aca2a0102d6c5babd09d3ecfb', '2026-04-18T11:43:51.793+00:00', N'success', N'', 0, N'ready', N'Tiểu Mỹ', '2026-04-18T11:43:51.793+00:00'),
(N'audio-36f7a142', N'poi', N'oc-oanh-1', N'vi', N'/storage/audio/pois/oc-oanh-1/vi/oc-oanh-1-vi-97ac46d28db0-20260424125850678.mp3', N'Bạn đang dừng chân tại Ốc Oanh 1, một tọa độ hải sản quen thuộc trên tuyến Vĩnh Khánh. Quầy ốc phía trước giúp bạn nhìn thấy nguyên liệu trước khi gọi món. Nếu thích vị chua ngọt, hãy chọn sò huyết rang me. Nếu thích mùi thơm béo, sò điệp nướng mỡ hành là lựa chọn rất dễ nhớ.', N'storage/audio/pois/oc-oanh-1/vi/oc-oanh-1-vi-97ac46d28db0-20260424125850678.mp3', N'oc-oanh-1-vi-97ac46d28db0-20260424125850678.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 16.7733125, 261268, N'97ac46d28db0e90961b42fbfe81136fff8b533a0ca14f7d2f350ef66ff061fbf', N'97ac46d28db0e90961b42fbfe81136fff8b533a0ca14f7d2f350ef66ff061fbf', '2026-04-22T16:37:37.931+00:00', N'success', N'', 0, N'ready', N'Tiểu Mỹ', '2026-04-22T16:37:37.931+00:00'),
(N'audio-9699ba63', N'poi', N'oc-oanh-1', N'zh-CN', N'/storage/audio/pois/oc-oanh-1/zh-CN/oc-oanh-1-zh-CN-a25a83910930-20260424132401467.mp3', N'您将在 Oc Oanh 1 停留，这是 Vinh Khanh 路线上熟悉的海鲜坐标。前面的蜗牛柜台可以帮助您在订购前查看成分。如果喜欢酸甜口味，可以选择罗望子烤蛤。如果你喜欢油腻的香味，洋葱油烤扇贝是一个非常难忘的选择。', N'storage/audio/pois/oc-oanh-1/zh-CN/oc-oanh-1-zh-CN-a25a83910930-20260424132401467.mp3', N'oc-oanh-1-zh-CN-a25a83910930-20260424132401467.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 20.27375, 328559, N'a25a839109305f1b35ae8d1dce0e492e0d11a79e7a02b62ad0b8814f9670b41d', N'a25a839109305f1b35ae8d1dce0e492e0d11a79e7a02b62ad0b8814f9670b41d', '2026-04-18T11:43:51.870+00:00', N'success', N'', 0, N'ready', N'Tiểu Mỹ', '2026-04-18T11:43:51.870+00:00'),
(N'audio-4866d6e7', N'poi', N'oc-phat', N'en', N'/storage/audio/pois/oc-phat/en/oc-phat-en-78e3b9642280-20260424132424799.mp3', N'You are at Oc Phat, one of the most accessible stops when starting to explore Vinh Khanh food street. Try salt-roasted snails, Thai steamed clams or stir-fried fingernail snails with garlic butter. The attraction of the restaurant is the bustling atmosphere, the dishes come out quickly and the scent of seafood spreads clearly in the evening of District 4.', N'storage/audio/pois/oc-phat/en/oc-phat-en-78e3b9642280-20260424132424799.mp3', N'oc-phat-en-78e3b9642280-20260424132424799.mp3', N'standard', N'generated', N'elevenlabs', N'EXAVITQu4vr4xnSDxMaL', N'eleven_flash_v2_5', N'mp3_44100_128', 22.703125, 333993, N'e53d825d5251', N'e53d825d5251', '2026-04-18T10:46:28.209+00:00', N'success', N'', 0, N'ready', N'Lê Quốc Bảo', '2026-04-18T10:46:28.209+00:00'),
(N'audio-e19db2bc', N'poi', N'oc-phat', N'ja', N'/storage/audio/pois/oc-phat/ja/oc-phat-ja-e5ae22214fb2-20260424132409739.mp3', N'ヴィン カイン フード ストリートの探索を開始する際に、最もアクセスしやすい停留所の 1 つであるオク ファットに着きます。カタツムリの塩焼き、タイ風蒸しアサリ、カタツムリのガーリックバター炒めなどをお試しください。レストランの魅力は、賑やかな雰囲気、料理が出てくるのが早く、4区の夕方にはっきりと広がる魚介の香りです。', N'storage/audio/pois/oc-phat/ja/oc-phat-ja-e5ae22214fb2-20260424132409739.mp3', N'oc-phat-ja-e5ae22214fb2-20260424132409739.mp3', N'standard', N'generated', N'elevenlabs', N'XrExE9yKIg1WjnnlVkGX', N'eleven_flash_v2_5', N'mp3_44100_128', 26.2296875, 427616, N'f8006173f77f', N'f8006173f77f', '2026-04-18T10:46:33.490+00:00', N'success', N'', 0, N'ready', N'Lê Quốc Bảo', '2026-04-18T10:46:33.490+00:00'),
(N'audio-d2bf5521', N'poi', N'oc-phat', N'ko', N'/storage/audio/pois/oc-phat/ko/oc-phat-ko-24b757cbe8a1-20260424132413535.mp3', N'Vinh Khanh 음식 거리를 탐험하기 시작할 때 가장 접근하기 쉬운 정류장 중 하나인 Oc Phat에 있습니다. 소금에 절인 달팽이, 태국식 조개찜, 마늘 버터를 곁들인 손톱 달팽이 볶음을 맛보세요. 4군 저녁에는 북적거리는 분위기와 음식이 빨리 나오고, 해산물 향이 은은하게 퍼지는 것이 이 식당의 매력이다.', N'storage/audio/pois/oc-phat/ko/oc-phat-ko-24b757cbe8a1-20260424132413535.mp3', N'oc-phat-ko-24b757cbe8a1-20260424132413535.mp3', N'standard', N'generated', N'elevenlabs', N'onwK4e9ZLuTAKqWW03F9', N'eleven_flash_v2_5', N'mp3_44100_128', 24.7145625, 458127, N'157e34dc0925', N'157e34dc0925', '2026-04-18T10:46:38.272+00:00', N'success', N'', 0, N'ready', N'Lê Quốc Bảo', '2026-04-18T10:46:38.272+00:00'),
(N'audio-4b5d7ade', N'poi', N'oc-phat', N'vi', N'/storage/audio/pois/oc-phat/vi/oc-phat-vi-ce78b21da241-20260424132421303.mp3', N'Bạn đang ở Ốc Phát, một trong những điểm dừng dễ tiếp cận nhất khi bắt đầu khám phá phố ẩm thực Vĩnh Khánh. Hãy thử các món ốc hương rang muối, nghêu hấp Thái hoặc ốc móng tay xào bơ tỏi. Điểm hấp dẫn của quán là không khí tụ tập rộn ràng, món lên nhanh và hương hải sản lan rất rõ trong buổi tối Quận 4.', N'storage/audio/pois/oc-phat/vi/oc-phat-vi-ce78b21da241-20260424132421303.mp3', N'oc-phat-vi-ce78b21da241-20260424132421303.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 21.266375, 285092, N'ce78b21da2416634a98a1713f754ae45318f4102a474c36efb9234c53b17600d', N'ce78b21da2416634a98a1713f754ae45318f4102a474c36efb9234c53b17600d', '2026-04-18T10:14:32.172+00:00', N'success', N'', 0, N'ready', N'Lê Quốc Bảo', '2026-04-18T10:14:32.172+00:00'),
(N'audio-975057de', N'poi', N'oc-phat', N'zh-CN', N'/storage/audio/pois/oc-phat/zh-CN/oc-phat-zh-CN-92193d408c81-20260424132417069.mp3', N'您已到达 Oc Phat，这是开始探索 Vinh Khanh 美食街时最方便到达的站点之一。尝试盐烤蜗牛、泰式蒸蛤蜊或蒜蓉黄油炒指甲蜗牛。这家餐厅的吸引力在于热闹的氛围，菜肴上桌速度很快，海鲜的香味在四区的夜晚清晰地弥漫开来。', N'storage/audio/pois/oc-phat/zh-CN/oc-phat-zh-CN-92193d408c81-20260424132417069.mp3', N'oc-phat-zh-CN-92193d408c81-20260424132417069.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 21.34475, 357817, N'15b19c16f023', N'15b19c16f023', '2026-04-18T11:31:55.436+00:00', N'success', N'', 0, N'ready', N'Lê Quốc Bảo', '2026-04-18T11:31:55.436+00:00'),
(N'audio-6e02efba', N'poi', N'sushi-ko', N'en', N'/storage/audio/pois/sushi-ko/en/sushi-ko-en-eab3333e241e-20260424132507192.mp3', N'Welcome to Sushi Ko Restaurant. This is a stop for those who love Japanese flavors in Vinh Khanh culinary street. The restaurant stands out with sushi, cold soba noodles and carefully prepared grilled fish dishes. Take a few minutes to feel the interesting contrast between the bustling street outside and the sophisticated culinary style inside.', N'storage/audio/pois/sushi-ko/en/sushi-ko-en-eab3333e241e-20260424132507192.mp3', N'sushi-ko-en-eab3333e241e-20260424132507192.mp3', N'standard', N'generated', N'elevenlabs', N'EXAVITQu4vr4xnSDxMaL', N'eleven_flash_v2_5', N'mp3_44100_128', 20.090875, 324380, N'eab3333e241e4cdd7c2a53ae72f3f5cda1025cb4d5f6303bd9a8c631e2f10261', N'eab3333e241e4cdd7c2a53ae72f3f5cda1025cb4d5f6303bd9a8c631e2f10261', '2026-04-18T11:34:23.769+00:00', N'success', N'', 0, N'ready', N'Minh Ánh', '2026-04-18T11:34:23.769+00:00'),
(N'audio-4978f607', N'poi', N'sushi-ko', N'ja', N'/storage/audio/pois/sushi-ko/ja/sushi-ko-ja-c658e5a9083c-20260424132510746.mp3', N'Sushi Ko Restaurantへようこそ。ここは、ヴィン カイン料理街にある日本の味を愛する人のための立ち寄りスポットです。レストランでは、寿司、冷たいそば、丁寧に調理された焼き魚料理が際立っています。数分かけて、外のにぎやかな通りと店内の洗練された料理スタイルの興味深いコントラストを感じてください。', N'storage/audio/pois/sushi-ko/ja/sushi-ko-ja-c658e5a9083c-20260424132510746.mp3', N'sushi-ko-ja-c658e5a9083c-20260424132510746.mp3', N'standard', N'generated', N'elevenlabs', N'XrExE9yKIg1WjnnlVkGX', N'eleven_flash_v2_5', N'mp3_44100_128', 24.2705, 395015, N'c658e5a9083c05791de39b25263e882225a818084224940c2d4877798050424a', N'c658e5a9083c05791de39b25263e882225a818084224940c2d4877798050424a', '2026-04-18T11:34:23.818+00:00', N'success', N'', 0, N'ready', N'Minh Ánh', '2026-04-18T11:34:23.818+00:00'),
(N'audio-d5c12952', N'poi', N'sushi-ko', N'ko', N'/storage/audio/pois/sushi-ko/ko/sushi-ko-ko-67ba60699d9a-20260424132503331.mp3', N'스시코 레스토랑에 오신 것을 환영합니다. Vinh Khanh 요리 거리에서 일본의 맛을 사랑하는 사람들을 위한 곳입니다. 스시, 냉소바, 정성스럽게 준비한 생선 구이 요리가 돋보이는 레스토랑입니다. 잠시 시간을 내어 외부의 번화한 거리와 내부의 세련된 요리 스타일 사이의 흥미로운 대조를 느껴보세요.', N'storage/audio/pois/sushi-ko/ko/sushi-ko-ko-67ba60699d9a-20260424132503331.mp3', N'sushi-ko-ko-67ba60699d9a-20260424132503331.mp3', N'standard', N'generated', N'elevenlabs', N'onwK4e9ZLuTAKqWW03F9', N'eleven_flash_v2_5', N'mp3_44100_128', 25.7594375, 423854, N'67ba60699d9a6a416806cd8cd0a32896fa6292aa782a8145558b05ddcb1eab22', N'67ba60699d9a6a416806cd8cd0a32896fa6292aa782a8145558b05ddcb1eab22', '2026-04-18T11:34:23.868+00:00', N'success', N'', 0, N'ready', N'Minh Ánh', '2026-04-18T11:34:23.868+00:00'),
(N'audio-3dbdd258', N'poi', N'sushi-ko', N'vi', N'/storage/audio/pois/sushi-ko/vi/sushi-ko-vi-2bd4986d94b3-20260424132517266.mp3', N'Chào mừng bạn đến với Nhà Hàng Sushi Ko. Đây là điểm dừng dành cho những ai yêu thích hương vị Nhật Bản giữa khu phố ẩm thực Vĩnh Khánh. Quán nổi bật với sushi, mì soba lạnh và các món cá nướng được chuẩn bị kỹ lưỡng. Hãy dành ít phút cảm nhận sự đối lập thú vị giữa nhịp phố sôi động bên ngoài và phong cách ẩm thực tinh tế bên trong.', N'storage/audio/pois/sushi-ko/vi/sushi-ko-vi-2bd4986d94b3-20260424132517266.mp3', N'sushi-ko-vi-2bd4986d94b3-20260424132517266.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 18.1839375, 291779, N'2bd4986d94b3afaadc87af7b948e5256715065cd9ecf12e455b3df7aeb1f1a5b', N'2bd4986d94b3afaadc87af7b948e5256715065cd9ecf12e455b3df7aeb1f1a5b', '2026-04-18T11:33:55.812+00:00', N'success', N'', 0, N'ready', N'Minh Ánh', '2026-04-18T11:33:55.812+00:00'),
(N'audio-420213fa', N'poi', N'sushi-ko', N'zh-CN', N'/storage/audio/pois/sushi-ko/zh-CN/sushi-ko-zh-CN-32573024f2ed-20260424132514107.mp3', N'欢迎光临 Sushi Ko 餐厅。对于那些喜欢日本风味的人来说，这是永庆美食街的一站。餐厅以寿司、冷荞麦面和精心准备的烤鱼菜肴而闻名。花几分钟时间感受一下外面熙熙攘攘的街道和里面精致的烹饪风格之间的有趣对比。', N'storage/audio/pois/sushi-ko/zh-CN/sushi-ko-zh-CN-32573024f2ed-20260424132514107.mp3', N'sushi-ko-zh-CN-32573024f2ed-20260424132514107.mp3', N'standard', N'generated', N'elevenlabs', N'JBFqnCBsd6RMkjVDRZzb', N'eleven_flash_v2_5', N'mp3_44100_128', 21.65825, 348204, N'32573024f2edba312679ba6ba98941274b999e506c8195461fdf4d528843caac', N'32573024f2edba312679ba6ba98941274b999e506c8195461fdf4d528843caac', '2026-04-18T11:34:23.949+00:00', N'success', N'', 0, N'ready', N'Minh Ánh', '2026-04-18T11:34:23.949+00:00');
GO


INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES
(N'ca-phe-che', N'cà phê'),
(N'ca-phe-che', N'chè'),
(N'ca-phe-che', N'tráng miệng'),
(N'oc-loan', N'gia đình'),
(N'oc-loan', N'hải sản'),
(N'oc-loan', N'ốc'),
(N'oc-oanh-1', N'gia đình'),
(N'oc-oanh-1', N'mỡ hành'),
(N'oc-oanh-1', N'ốc'),
(N'oc-phat', N'hải sản'),
(N'oc-phat', N'ốc'),
(N'oc-phat', N'tụ tập'),
(N'sushi-ko', N'hải sản'),
(N'sushi-ko', N'món Nhật'),
(N'sushi-ko', N'sushi');
GO

INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl) VALUES
(N'food-che-khuc-bach', N'ca-phe-che', N'Matcha Latte', N'Matcha Latte được làm từ bột matcha Nhật Bản, thức uống phù hợp với các bạn gen Z trẻ trung năng động.', N'35.000 VND', N'/storage/images/food-items/20260418114004257-3b8fb21766a54283bdb3922680752610.jpg'),
(N'food-37e487ed', N'oc-loan', N'Ốc bươu nướng muối ớt', N'', N'80.000', N'/storage/images/food-items/20260420082517595-ab9d950d953b4a8f820734d5a1f9e91f.jpg'),
(N'food-so-diep', N'oc-oanh-1', N'Sò điệp nướng mỡ hành', N'Sò điệp nướng thơm, phủ mỡ hành và đậu phộng rang.', N'40.000 VND', N'/storage/images/food-items/20260418114249365-62422ad719d84075aa07c44621da3a08.jpg'),
(N'food-oc-giac', N'oc-phat', N'Ốc giác xào rau muống', N'Ốc giác thái miếng, xào cùng rau muống giòn và nước sốt đậm vị.', N'129.000 VND', N'/storage/images/food-items/20260418101318762-9877e771be5c4d71bb025fc66ac3040c.jpg'),
(N'food-oc-mong-tay', N'oc-phat', N'Ốc móng tay xào bơ tỏi', N'Ốc móng tay xào nhanh với bơ, tỏi và rau thơm, hợp dùng cùng bia lạnh.', N'79.000 VND', N'/storage/images/food-items/20260418101330404-89fc4289c8ab488d935ea06bb814b4ee.webp'),
(N'food-sushi-kajiki', N'sushi-ko', N'Kajiki Maguro Yaki', N'Cá kiếm nướng kiểu Nhật, ướp sốt đậm nhẹ và nướng vừa chín.', N'138.000 VND', N'/storage/images/food-items/20260418113323609-bdbd6dc0c1d04469865ddafb49c2dfe6.jpg'),
(N'food-sushi-zaru-soba', N'sushi-ko', N'Zaru Soba', N'Mì soba lạnh đặt trên khay tre, dùng kèm nước chấm thanh nhẹ.', N'142.000 VND', N'/storage/images/food-items/20260418113329577-0dd004c0b7f04a02b74200e08df5d5f9.jpg');
GO

INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES
(N'media-2afc6fd7', N'poi', N'oc-loan', N'image', N'/storage/images/pois/20260420082328041-b9759e3ad25f4a2e9c6164987fbd3e52.jpg', N'Ảnh đại diện Nhà hàng Ốc Loan', '2026-04-20T08:25:49.8304662+00:00'),
(N'media-ca-phe-che', N'poi', N'ca-phe-che', N'image', N'/storage/images/pois/20260418113747792-d2631d25bd8f42b2a2ffad2d2ffc1eca.jpeg', N'Món chè và đồ ngọt tại Cà phê Chè', '2026-04-18T12:40:21.7468261+07:00'),
(N'media-oc-oanh', N'poi', N'oc-oanh-1', N'image', N'/storage/images/pois/20260418114238581-5458cbb19c3c4207b0a6cb246ac275e9.jpg', N'Quầy hải sản tại Ốc Oanh 1', '2026-04-18T12:40:21.7468261+07:00'),
(N'media-oc-phat', N'poi', N'oc-phat', N'image', N'/storage/images/pois/20260418101246920-8ad4ae1cabbe427bbb224e5e4ac2abfb.jpg', N'Hải sản và các món ốc tại Ốc Phát', '2026-04-18T12:40:21.7468261+07:00'),
(N'media-sushi-ko', N'poi', N'sushi-ko', N'image', N'/storage/images/pois/20260418113300732-db188c6d687646b396ddffbdee8b783f.jpg', N'Không gian và món sushi tại Sushi Ko', '2026-04-18T12:40:21.7468261+07:00');
GO

INSERT INTO dbo.Routes (Id, Name, [Description], IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt) VALUES
(N'route-15a5dcb3', N'Tour thuần ốc hải sản', N'Chào bạn, cùng bắt đầu chuyến đi ăn ốc ở đường Vĩnh Khánh nhé! Hôm nay, chúng ta sẽ dạo qua 3 quán ốc lúc nào cũng đông khách tại đây:

Điểm số 1 - Ốc Phát: Điểm dừng đầu tiên của chúng ta là Ốc Phát. Chỗ này có không gian ngồi rất thoáng mát và thoải mái. Các món như ốc móng tay xào rau muống hay sò lông nướng mỡ hành ở đây làm rất vừa miệng, ăn một lần là nhớ.

Điểm số 2 - Ốc Oanh: Đi tiếp một đoạn, bạn sẽ thấy Ốc Oanh. Quán này rất rộng và sáng sủa. Bạn nhớ gọi thử món càng ghẹ rang muối ớt hoặc ốc hương nướng muối nhé. Đồ ăn dọn ra nhanh và vị cay mặn cực kỳ ngon.

Điểm số 3 - Ốc Loan: Cuối cùng, chúng ta sẽ đến Ốc Loan. Quán hơi đông một chút nhưng bù lại hải sản lúc nào cũng tươi rói. Sò huyết xào me chua ngọt và sụn gà chiên giòn rụm là hai món được gọi nhiều nhất ở đây.', 0, 1, 1, NULL, N'Ánh Xuân', '2026-04-20T08:35:59.5288460+00:00'),
(N'route-balanced', N'Tour đổi vị Vĩnh Khánh', N'Lộ trình kết hợp món Nhật, ốc và món tráng miệng để khách mới có cái nhìn cân bằng về khu phố.', 0, 1, 1, NULL, N'Ánh Xuân', '2026-04-18T12:40:21.7468261+07:00'),
(N'route-evening-seafood', N'Tour hải sản buổi tối', N'Lộ trình ngắn đi qua các quán ốc tiêu biểu, phù hợp nhóm bạn muốn cảm nhận nhịp phố Vĩnh Khánh về đêm.', 1, 1, 1, NULL, N'Ánh Xuân', '2026-04-19T14:57:19.8639375+00:00');
GO

INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES
(N'route-15a5dcb3', 1, N'oc-phat'),
(N'route-15a5dcb3', 2, N'oc-oanh-1'),
(N'route-15a5dcb3', 3, N'oc-loan'),
(N'route-balanced', 1, N'sushi-ko'),
(N'route-balanced', 2, N'oc-phat'),
(N'route-balanced', 3, N'ca-phe-che'),
(N'route-evening-seafood', 1, N'oc-oanh-1'),
(N'route-evening-seafood', 2, N'oc-phat'),
(N'route-evening-seafood', 3, N'ca-phe-che');
GO

INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status], VisibleFrom, CreatedByUserId, OwnerUserId, IsDeleted) VALUES
(N'promo-ca-phe-che-sweet', N'ca-phe-che', N'Tặng topping chè', N'Tặng một phần topping khi gọi combo cà phê và chè khúc bạch.', '2026-04-15T12:40:21.7542574+07:00', '2026-05-09T12:40:21.7542574+07:00', N'active', '2026-04-15T12:40:21.7542574+07:00', N'user-super', N'user-owner-dessert', 0),
(N'promo-oc-phat-combo', N'oc-phat', N'Combo ốc cho nhóm 4 người', N'Ưu đãi combo gồm ba món ốc bán chạy và một phần nghêu hấp Thái cho nhóm bạn.', '2026-04-11T12:40:21.7542574+07:00', '2026-05-18T12:40:21.7542574+07:00', N'active', '2026-04-11T12:40:21.7542574+07:00', N'user-super', N'user-owner-oc', 0);
GO

INSERT INTO dbo.SystemSettings (
    Id, AppName, SupportEmail, SupportPhone, ContactAddress, SupportInstructions,
    SupportHours, ContactUpdatedAtUtc,
    DefaultLanguage, FallbackLanguage, StorageProvider,
    GeofenceRadiusMeters, AnalyticsRetentionDays
) VALUES
(1, N'Hệ thống quản trị thuyết minh Vĩnh Khánh', N'support@vinhkhanh.vn', N'0900000000',
 N'Vinh Khanh Food Street, Ho Chi Minh City',
 N'Vui lòng liên hệ bộ phận hỗ trợ nếu bạn cần khiếu nại hoặc cần trợ giúp.',
 N'08:00 - 21:00 hằng ngày',
 SYSDATETIMEOFFSET(),
 N'vi', N'en', N'local', 60, 180);
GO

INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES
(1, N'supported', N'en'),
(1, N'supported', N'ja'),
(1, N'supported', N'ko'),
(1, N'supported', N'vi'),
(1, N'supported', N'zh-CN');
GO
