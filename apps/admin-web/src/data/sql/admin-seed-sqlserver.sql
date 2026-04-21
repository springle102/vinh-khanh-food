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
(N'cat-bbq', N'Nướng & Beer', N'nuong-beer', N'flame', N'#de6245'),
(N'cat-coffee-tea', N'Cà phê & tráng miệng', N'ca-phe-trang-mieng', N'coffee', N'#8b5e3c'),
(N'cat-oc', N'Ốc & Hải sản', N'oc-hai-san', N'shell', N'#f97316'),
(N'cat-seafood-raw', N'Hải sản & món Nhật', N'hai-san-mon-nhat', N'fish', N'#0f766e');
GO

INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt) VALUES
(N'user-faf2da21', N'Vương Thanh', N'thanh@gmail.com', N'0325417896', N'PLACE_OWNER', N'thanh@123', N'active', '2026-04-20T08:17:47.7851657+00:00', '2026-04-20T09:38:34.3231193+00:00', N'#f97316', NULL, N'approved', NULL, '2026-04-20T08:17:47.7851657+00:00', '2026-04-20T08:18:08.2780498+00:00'),
(N'user-owner-dessert', N'Phạm Mỹ Linh', N'mylinh@gmail.com', N'0909 188 004', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T11:36:21.4316485+00:00', N'#8b5e3c', N'ca-phe-che', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-owner-oanh', N'Tiểu Mỹ', N'tieumy123@gmail.com', N'0952 478 925', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-20T09:17:01.2195002+00:00', N'#d97706', N'oc-oanh-1', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-owner-oc', N'Lê Quốc Bảo', N'quocbao@gmail.com', N'0909 188 003', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-20T08:50:15.0402116+00:00', N'#f97316', N'oc-phat', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-owner-sushi', N'Minh Ánh', N'minhanh@gmail.com', N'0909 188 002', N'PLACE_OWNER', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T11:32:36.8803917+00:00', N'#0f766e', N'sushi-ko', N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00'),
(N'user-super', N'Ánh Xuân', N'anhxuan@gmail.com', N'0909 188 001', N'SUPER_ADMIN', N'Admin@123', N'active', '2026-04-18T12:40:21.7153645+07:00', '2026-04-20T09:39:40.3227059+00:00', N'#f97316', NULL, N'approved', NULL, '2026-04-18T12:40:21.7153645+07:00', '2026-04-18T12:40:21.7153645+07:00');
GO

INSERT INTO dbo.Pois (Id, Slug, Title, ShortDescription, [Description], AudioScript, SourceLanguageCode, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, TriggerRadius, Priority, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt, IsActive, LockedBySuperAdmin, ApprovedAt, RejectionReason, RejectedAt) VALUES
(N'ca-phe-che', N'ca-phe-che', N'Cà phê Chè', N'Quán tráng miệng và cà phê nhẹ nhàng, phù hợp nghỉ chân sau khi ăn hải sản.', N'Chào mừng bạn đến với Cà phê Chè. Sau những món ốc và hải sản đậm vị, đây là nơi phù hợp để nghỉ chân bằng một ly cà phê hoặc chén chè mát. Không gian nhẹ nhàng, dễ ngồi trò chuyện và rất hợp để kết thúc hành trình ẩm thực Vĩnh Khánh bằng một vị ngọt dễ chịu.', N'Chào mừng bạn đến với Cà phê Chè. Sau những món ốc và hải sản đậm vị, đây là nơi phù hợp để nghỉ chân bằng một ly cà phê hoặc chén chè mát. Không gian nhẹ nhàng, dễ ngồi trò chuyện và rất hợp để kết thúc hành trình ẩm thực Vĩnh Khánh bằng một vị ngọt dễ chịu.', N'vi', N'Chung cư H1, Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM', 10.762195, 106.701499, N'cat-coffee-tea', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'25.000 - 75.000 VND', 20, 2, N'user-owner-dessert', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-18T11:45:45.7649279+00:00', 1, 0, '2026-04-18T11:45:45.7629361+00:00', NULL, NULL),
(N'oc-loan', N'nha-hang-oc-loan', N'Nhà hàng Ốc Loan', N'', N'Điểm cộng lớn nhất của Ốc Loan là hải sản luôn tươi rói, các loại ốc to, mập mạp và được nêm nếm rất đậm đà, thiên một chút xíu về khẩu vị ngọt đặc trưng của miền Nam. Nếu có dịp ghé quán, bạn nhất định nên gọi thử những món quen thuộc nhưng cực kỳ "bắt miệng" như: sò huyết xào me chua ngọt sền sệt, ốc hương xào bơ tỏi thơm lừng chấm kèm bánh mì, hay một phần sụn gà chiên nước mắm giòn rụm để nhâm nhi.', N'Điểm cộng lớn nhất của Ốc Loan là hải sản luôn tươi rói, các loại ốc to, mập mạp và được nêm nếm rất đậm đà, thiên một chút xíu về khẩu vị ngọt đặc trưng của miền Nam. Nếu có dịp ghé quán, bạn nhất định nên gọi thử những món quen thuộc nhưng cực kỳ "bắt miệng" như: sò huyết xào me chua ngọt sền sệt, ốc hương xào bơ tỏi thơm lừng chấm kèm bánh mì, hay một phần sụn gà chiên nước mắm giòn rụm để nhâm nhi.', N'vi', N'Nhà Hàng Ốc Loan, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.761159, 106.702695, N'cat-oc', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'50.000-150.000', 25, 4, N'user-faf2da21', N'Ánh Xuân', '2026-04-20T08:25:46.0942429+00:00', '2026-04-20T09:38:59.4688044+00:00', 1, 0, '2026-04-20T09:19:25.8639560+00:00', NULL, NULL),
(N'oc-oanh-1', N'oc-oanh-1', N'Ốc Oanh 1', N'Tọa độ ốc quen thuộc với quầy hải sản tươi và nhiều món sốt đậm vị Nam Bộ.', N'Bạn đang dừng chân tại Ốc Oanh 1, một tọa độ hải sản quen thuộc trên tuyến Vĩnh Khánh. Quầy ốc phía trước giúp bạn nhìn thấy nguyên liệu trước khi gọi món. Nếu thích vị chua ngọt, hãy chọn sò huyết rang me. Nếu thích mùi thơm béo, sò điệp nướng mỡ hành là lựa chọn rất dễ nhớ.', N'Bạn đang dừng chân tại Ốc Oanh 1, một tọa độ hải sản quen thuộc trên tuyến Vĩnh Khánh. Quầy ốc phía trước giúp bạn nhìn thấy nguyên liệu trước khi gọi món. Nếu thích vị chua ngọt, hãy chọn sò huyết rang me. Nếu thích mùi thơm béo, sò điệp nướng mỡ hành là lựa chọn rất dễ nhớ.', N'vi', N'534 hẻm 42/237 Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM', 10.761415, 106.702834, N'cat-oc', N'published', 0, N'Quận 4', N'Phường Khánh Hội', N'40.000 - 120.000 VND', 25, 3, N'user-owner-oanh', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-18T11:45:43.5732109+00:00', 1, 0, '2026-04-18T11:45:43.5718264+00:00', NULL, NULL),
(N'oc-phat', N'oc-phat', N'Ốc Phát', N'Quán ốc đầu phố với thực đơn hải sản đa dạng, phù hợp nhóm bạn và khách lần đầu đến Vĩnh Khánh.', N'Bạn đang ở Ốc Phát, một trong những điểm dừng dễ tiếp cận nhất khi bắt đầu khám phá phố ẩm thực Vĩnh Khánh. Hãy thử các món ốc hương rang muối, nghêu hấp Thái hoặc ốc móng tay xào bơ tỏi. Điểm hấp dẫn của quán là không khí tụ tập rộn ràng, món lên nhanh và hương hải sản lan rất rõ trong buổi tối Quận 4.', N'Bạn đang ở Ốc Phát, một trong những điểm dừng dễ tiếp cận nhất khi bắt đầu khám phá phố ẩm thực Vĩnh Khánh. Hãy thử các món ốc hương rang muối, nghêu hấp Thái hoặc ốc móng tay xào bơ tỏi. Điểm hấp dẫn của quán là không khí tụ tập rộn ràng, món lên nhanh và hương hải sản lan rất rõ trong buổi tối Quận 4.', N'vi', N'Ốc Phát, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam', 10.761902, 106.702158, N'cat-oc', N'published', 1, N'Quận 4', N'Phường Khánh Hội', N'50.000 - 110.000 VND', 30, 5, N'user-owner-oc', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-20T08:50:15.2450584+00:00', 1, 0, '2026-04-18T11:35:31.3429292+00:00', NULL, NULL),
(N'sushi-ko', N'nha-hang-sushi-ko', N'Nhà Hàng Sushi Ko', N'Điểm dừng món Nhật gần phố ẩm thực Vĩnh Khánh, nổi bật với sushi và hải sản chế biến tươi.', N'Chào mừng bạn đến với Nhà Hàng Sushi Ko. Đây là điểm dừng dành cho những ai yêu thích hương vị Nhật Bản giữa khu phố ẩm thực Vĩnh Khánh. Quán nổi bật với sushi, mì soba lạnh và các món cá nướng được chuẩn bị kỹ lưỡng. Hãy dành ít phút cảm nhận sự đối lập thú vị giữa nhịp phố sôi động bên ngoài và phong cách ẩm thực tinh tế bên trong.', N'Chào mừng bạn đến với Nhà Hàng Sushi Ko. Đây là điểm dừng dành cho những ai yêu thích hương vị Nhật Bản giữa khu phố ẩm thực Vĩnh Khánh. Quán nổi bật với sushi, mì soba lạnh và các món cá nướng được chuẩn bị kỹ lưỡng. Hãy dành ít phút cảm nhận sự đối lập thú vị giữa nhịp phố sôi động bên ngoài và phong cách ẩm thực tinh tế bên trong.', N'vi', N'122/37/15 Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM', 10.760772, 106.704798, N'cat-seafood-raw', N'published', 1, N'Quận 4', N'Phường Khánh Hội', N'50.000 - 150.000 VND', 25, 4, N'user-owner-sushi', N'Ánh Xuân', '2026-04-18T12:40:21.7342243+07:00', '2026-04-18T11:35:24.0691280+00:00', 1, 0, '2026-04-18T11:35:24.0661212+00:00', NULL, NULL);
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
(N'food-che-khuc-bach', N'ca-phe-che', N'Matcha Latte', N'Matcha Latte được làm từ bột matcha Nhật Bản, thức uống phù hợp với các bạn gen Z trẻ trung năng động.', N'35.000 VND', N'http://127.0.0.1:5080/storage/images/food-items/20260418114004257-3b8fb21766a54283bdb3922680752610.jpg'),
(N'food-37e487ed', N'oc-loan', N'Ốc bươu nướng muối ớt', N'', N'80.000', N'http://localhost:5080/storage/images/food-items/20260420082517595-ab9d950d953b4a8f820734d5a1f9e91f.jpg'),
(N'food-so-diep', N'oc-oanh-1', N'Sò điệp nướng mỡ hành', N'Sò điệp nướng thơm, phủ mỡ hành và đậu phộng rang.', N'40.000 VND', N'http://127.0.0.1:5080/storage/images/food-items/20260418114249365-62422ad719d84075aa07c44621da3a08.jpg'),
(N'food-oc-giac', N'oc-phat', N'Ốc giác xào rau muống', N'Ốc giác thái miếng, xào cùng rau muống giòn và nước sốt đậm vị.', N'129.000 VND', N'http://127.0.0.1:5080/storage/images/food-items/20260418101318762-9877e771be5c4d71bb025fc66ac3040c.jpg'),
(N'food-oc-mong-tay', N'oc-phat', N'Ốc móng tay xào bơ tỏi', N'Ốc móng tay xào nhanh với bơ, tỏi và rau thơm, hợp dùng cùng bia lạnh.', N'79.000 VND', N'http://127.0.0.1:5080/storage/images/food-items/20260418101330404-89fc4289c8ab488d935ea06bb814b4ee.webp'),
(N'food-sushi-kajiki', N'sushi-ko', N'Kajiki Maguro Yaki', N'Cá kiếm nướng kiểu Nhật, ướp sốt đậm nhẹ và nướng vừa chín.', N'138.000 VND', N'http://127.0.0.1:5080/storage/images/food-items/20260418113323609-bdbd6dc0c1d04469865ddafb49c2dfe6.jpg'),
(N'food-sushi-zaru-soba', N'sushi-ko', N'Zaru Soba', N'Mì soba lạnh đặt trên khay tre, dùng kèm nước chấm thanh nhẹ.', N'142.000 VND', N'http://127.0.0.1:5080/storage/images/food-items/20260418113329577-0dd004c0b7f04a02b74200e08df5d5f9.jpg');
GO

INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES
(N'media-2afc6fd7', N'poi', N'oc-loan', N'image', N'http://localhost:5080/storage/images/pois/20260420082328041-b9759e3ad25f4a2e9c6164987fbd3e52.jpg', N'Ảnh đại diện Nhà hàng Ốc Loan', '2026-04-20T08:25:49.8304662+00:00'),
(N'media-ca-phe-che', N'poi', N'ca-phe-che', N'image', N'http://127.0.0.1:5080/storage/images/pois/20260418113747792-d2631d25bd8f42b2a2ffad2d2ffc1eca.jpeg', N'Món chè và đồ ngọt tại Cà phê Chè', '2026-04-18T12:40:21.7468261+07:00'),
(N'media-oc-oanh', N'poi', N'oc-oanh-1', N'image', N'http://127.0.0.1:5080/storage/images/pois/20260418114238581-5458cbb19c3c4207b0a6cb246ac275e9.jpg', N'Quầy hải sản tại Ốc Oanh 1', '2026-04-18T12:40:21.7468261+07:00'),
(N'media-oc-phat', N'poi', N'oc-phat', N'image', N'http://127.0.0.1:5080/storage/images/pois/20260418101246920-8ad4ae1cabbe427bbb224e5e4ac2abfb.jpg', N'Hải sản và các món ốc tại Ốc Phát', '2026-04-18T12:40:21.7468261+07:00'),
(N'media-sushi-ko', N'poi', N'sushi-ko', N'image', N'http://127.0.0.1:5080/storage/images/pois/20260418113300732-db188c6d687646b396ddffbdee8b783f.jpg', N'Không gian và món sushi tại Sushi Ko', '2026-04-18T12:40:21.7468261+07:00');
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

INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status]) VALUES
(N'promo-ca-phe-che-sweet', N'ca-phe-che', N'Tặng topping chè', N'Tặng một phần topping khi gọi combo cà phê và chè khúc bạch.', '2026-04-15T12:40:21.7542574+07:00', '2026-05-09T12:40:21.7542574+07:00', N'active'),
(N'promo-oc-phat-combo', N'oc-phat', N'Combo ốc cho nhóm 4 người', N'Ưu đãi combo gồm ba món ốc bán chạy và một phần nghêu hấp Thái cho nhóm bạn.', '2026-04-11T12:40:21.7542574+07:00', '2026-05-18T12:40:21.7542574+07:00', N'active');
GO

INSERT INTO dbo.SystemSettings (Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage, MapProvider, StorageProvider, TtsProvider, GeofenceRadiusMeters, AnalyticsRetentionDays) VALUES
(1, N'Hệ thống quản trị thuyết minh Vĩnh Khánh', N'support@vinhkhanh.vn', N'vi', N'en', N'openstreetmap', N'local', N'elevenlabs', 60, 180);
GO

INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES
(1, N'supported', N'en'),
(1, N'supported', N'ja'),
(1, N'supported', N'ko'),
(1, N'supported', N'vi'),
(1, N'supported', N'zh-CN');
GO
