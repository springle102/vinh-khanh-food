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
IF OBJECT_ID(N'dbo.UserPoiVisits', N'U') IS NOT NULL
    DROP TABLE dbo.UserPoiVisits;
GO
IF OBJECT_ID(N'dbo.AudioListenLogs', N'U') IS NOT NULL
    DROP TABLE dbo.AudioListenLogs;
GO
IF OBJECT_ID(N'dbo.ViewLogs', N'U') IS NOT NULL
    DROP TABLE dbo.ViewLogs;
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
    ManagedPoiId NVARCHAR(50) NULL
);
GO

CREATE TABLE dbo.CustomerUsers (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    Name NVARCHAR(120) NOT NULL,
    Email NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(30) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    IsActive BIT NOT NULL,
    IsBanned BIT NOT NULL,
    PreferredLanguage NVARCHAR(20) NOT NULL,
    IsPremium BIT NOT NULL,
    TotalScans INT NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    LastActiveAt DATETIMEOFFSET(7) NULL,
    Username NVARCHAR(120) NULL,
    DeviceId NVARCHAR(200) NULL,
    Country NVARCHAR(20) NOT NULL,
    DeviceType NVARCHAR(20) NOT NULL,
    CONSTRAINT CK_CustomerUsers_Status CHECK ([Status] IN (N'active', N'inactive', N'banned')),
    CONSTRAINT CK_CustomerUsers_DeviceType CHECK (DeviceType IN (N'android', N'ios')),
    CONSTRAINT CK_CustomerUsers_Identity CHECK (
        NULLIF(LTRIM(RTRIM(Username)), N'') IS NOT NULL OR
        NULLIF(LTRIM(RTRIM(DeviceId)), N'') IS NOT NULL
    )
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
    AverageVisitDurationMinutes INT NOT NULL,
    PopularityScore INT NOT NULL,
    OwnerUserId NVARCHAR(50) NULL,
    UpdatedBy NVARCHAR(120) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_Pois_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(Id),
    CONSTRAINT FK_Pois_AdminUsers FOREIGN KEY (OwnerUserId) REFERENCES dbo.AdminUsers(Id)
);
GO

CREATE TABLE dbo.CustomerFavoritePois (
    CustomerUserId NVARCHAR(50) NOT NULL,
    PoiId NVARCHAR(50) NOT NULL,
    PRIMARY KEY (CustomerUserId, PoiId),
    CONSTRAINT FK_CustomerFavoritePois_CustomerUsers FOREIGN KEY (CustomerUserId) REFERENCES dbo.CustomerUsers(Id),
    CONSTRAINT FK_CustomerFavoritePois_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO

CREATE TABLE dbo.UserPoiVisits (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    UserId NVARCHAR(50) NOT NULL,
    PoiId NVARCHAR(50) NOT NULL,
    VisitedAt DATETIMEOFFSET(7) NOT NULL,
    TranslatedLanguage NVARCHAR(20) NOT NULL,
    CONSTRAINT FK_UserPoiVisits_CustomerUsers FOREIGN KEY (UserId) REFERENCES dbo.CustomerUsers(Id),
    CONSTRAINT FK_UserPoiVisits_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
);
GO
CREATE INDEX IX_UserPoiVisits_UserId_VisitedAt
    ON dbo.UserPoiVisits (UserId, VisitedAt DESC);
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
    Theme NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    DurationMinutes INT NOT NULL,
    Difficulty NVARCHAR(30) NOT NULL,
    CoverImageUrl NVARCHAR(500) NOT NULL,
    IsFeatured BIT NOT NULL,
    IsActive BIT NOT NULL,
    UpdatedBy NVARCHAR(120) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL
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

CREATE TABLE dbo.AuditLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    ActorName NVARCHAR(120) NOT NULL,
    ActorRole NVARCHAR(30) NOT NULL,
    [Action] NVARCHAR(200) NOT NULL,
    TargetValue NVARCHAR(300) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL
);
GO

CREATE TABLE dbo.RefreshSessions (
    RefreshToken NVARCHAR(200) NOT NULL PRIMARY KEY,
    UserId NVARCHAR(50) NOT NULL,
    ExpiresAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_RefreshSessions_AdminUsers FOREIGN KEY (UserId) REFERENCES dbo.AdminUsers(Id)
);
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
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-culture', N'Điểm văn hóa', N'diem-van-hoa', N'landmark', N'#9a3412');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-dessert', N'Tráng miệng', N'trang-mieng', N'sparkle', N'#d9a845');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-oc', N'Ốc & Hải sản', N'oc-hai-san', N'shell', N'#f97316');
GO
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId) VALUES (N'user-owner-bbq', N'Minh Ánh', N'bbq@vinhkhanh.vn', N'0909 188 002', N'PLACE_OWNER', N'Admin@123', N'active', CAST(N'2025-12-14T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-21T11:21:10.8821655+00:00' AS datetimeoffset(7)), N'#de6245', N'sushi-ko');
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId) VALUES (N'user-owner-dessert', N'Phạm Mỹ Linh', N'che@vinhkhanh.vn', N'0909 188 004', N'PLACE_OWNER', N'Admin@123', N'locked', CAST(N'2026-01-05T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-12T07:20:00.0000000+00:00' AS datetimeoffset(7)), N'#7c2d12', N'poi-sweet-lane');
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId) VALUES (N'user-owner-oc', N'Lê Quốc Bảo', N'oc@vinhkhanh.vn', N'0909 188 003', N'PLACE_OWNER', N'Admin@123', N'active', CAST(N'2025-12-29T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-18T11:22:00.0000000+00:00' AS datetimeoffset(7)), N'#d9a845', N'poi-snail-signature');
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId) VALUES (N'user-super', N'Ánh Xuân', N'superadmin@vinhkhanh.vn', N'0909 188 001', N'SUPER_ADMIN', N'Admin@123', N'active', CAST(N'2025-11-19T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-05T10:21:51.3650702+00:00' AS datetimeoffset(7)), N'#f97316', NULL);
GO
INSERT INTO dbo.CustomerUsers (Id, Name, Email, Phone, [Status], IsActive, IsBanned, PreferredLanguage, IsPremium, TotalScans, CreatedAt, LastActiveAt, Username, DeviceId, Country, DeviceType) VALUES (N'customer-1', N'Nguyễn Bảo Vy', N'baovy@gmail.com', N'0911 000 111', N'active', 1, 0, N'vi', 0, 12, CAST(N'2026-01-10T03:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-19T03:15:00.0000000+00:00' AS datetimeoffset(7)), N'bao.vy', N'android-baovy-001', N'VN', N'android');
INSERT INTO dbo.CustomerUsers (Id, Name, Email, Phone, [Status], IsActive, IsBanned, PreferredLanguage, IsPremium, TotalScans, CreatedAt, LastActiveAt, Username, DeviceId, Country, DeviceType) VALUES (N'customer-2', N'Lucas Martin', N'lucas@example.com', N'+84 901 111 222', N'inactive', 0, 0, N'en', 1, 24, CAST(N'2026-01-15T03:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-19T05:20:00.0000000+00:00' AS datetimeoffset(7)), N'lucas.martin', N'ios-lucas-002', N'FR', N'ios');
INSERT INTO dbo.CustomerUsers (Id, Name, Email, Phone, [Status], IsActive, IsBanned, PreferredLanguage, IsPremium, TotalScans, CreatedAt, LastActiveAt, Username, DeviceId, Country, DeviceType) VALUES (N'customer-3', N'Kim Seo Yoon', N'seoyoon@example.com', N'+84 902 222 333', N'banned', 1, 1, N'ko', 1, 8, CAST(N'2026-02-02T03:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-17T09:05:00.0000000+00:00' AS datetimeoffset(7)), N'seoyoon', N'ios-seoyoon-003', N'KR', N'ios');
GO
INSERT INTO dbo.Pois (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt) VALUES (N'poi-snail-signature', N'quan-oc-vinh-khanh-signature', N'42 Vĩnh Khánh, Phường Khánh Hội, TP.HCM', 10.758030, 106.701620, N'cat-oc', N'published', 1, N'Quận 4', N'Khánh Hội', N'80.000 - 280.000 VND', 45, 98, N'user-owner-oc', N'Minh Ánh', CAST(N'2025-11-19T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-19T09:25:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.Pois (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt) VALUES (N'poi-sweet-lane', N'hem-che-vinh-khanh', N'88/4 Vĩnh Khánh, Phường Vĩnh Hội, TP.HCM', 10.757120, 106.703020, N'cat-dessert', N'draft', 0, N'Quận 4', N'Vĩnh Hội', N'25.000 - 75.000 VND', 25, 73, N'user-owner-dessert', N'Phạm Mỹ Linh', CAST(N'2026-01-16T02:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-16T07:10:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.Pois (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt) VALUES (N'sushi-ko', N'nha-hang-sushi-ko', N'Nhà Hàng Sushi Ko, 122/37/15 Vĩnh Khánh, Phường Khánh Hội, Thành phố Thủ Đức, Việt Nam', 10.760772, 106.704798, N'cat-bbq', N'published', 1, N'Thành phố Thủ Đức', N'Phường Khánh Hội', N'50.000-150.000', 50, 96, N'user-owner-bbq', N'Ánh Xuân', CAST(N'2025-11-19T03:15:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-04-05T10:24:38.6159952+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.UserPoiVisits (Id, UserId, PoiId, VisitedAt, TranslatedLanguage) VALUES (N'visit-1', N'customer-1', N'poi-snail-signature', CAST(N'2026-03-18T05:40:00.0000000+00:00' AS datetimeoffset(7)), N'vi');
INSERT INTO dbo.UserPoiVisits (Id, UserId, PoiId, VisitedAt, TranslatedLanguage) VALUES (N'visit-2', N'customer-1', N'sushi-ko', CAST(N'2026-03-19T03:15:00.0000000+00:00' AS datetimeoffset(7)), N'en');
INSERT INTO dbo.UserPoiVisits (Id, UserId, PoiId, VisitedAt, TranslatedLanguage) VALUES (N'visit-3', N'customer-2', N'sushi-ko', CAST(N'2026-03-19T05:20:00.0000000+00:00' AS datetimeoffset(7)), N'en');
INSERT INTO dbo.UserPoiVisits (Id, UserId, PoiId, VisitedAt, TranslatedLanguage) VALUES (N'visit-4', N'customer-3', N'poi-snail-signature', CAST(N'2026-03-17T09:05:00.0000000+00:00' AS datetimeoffset(7)), N'ko');
GO
INSERT INTO dbo.CustomerFavoritePois (CustomerUserId, PoiId) VALUES (N'customer-1', N'poi-snail-signature');
INSERT INTO dbo.CustomerFavoritePois (CustomerUserId, PoiId) VALUES (N'customer-1', N'sushi-ko');
INSERT INTO dbo.CustomerFavoritePois (CustomerUserId, PoiId) VALUES (N'customer-2', N'sushi-ko');
INSERT INTO dbo.CustomerFavoritePois (CustomerUserId, PoiId) VALUES (N'customer-3', N'poi-snail-signature');
GO
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'poi-snail-signature', N'đặc sản');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'poi-snail-signature', N'gia đình');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'poi-snail-signature', N'ốc');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'poi-sweet-lane', N'ăn vặt');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'poi-sweet-lane', N'chè');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'poi-sweet-lane', N'gia đình');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'sushi-ko', N'đồ sống');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'sushi-ko', N'hải sản');
INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (N'sushi-ko', N'sushi');
GO
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-1', N'poi-snail-signature', N'Ốc hương xào bơ tỏi', N'Món bán chạy với sốt bơ tỏi đậm vị, thơm và ngậy.', N'95.000 - 140.000 VND', N'https://images.unsplash.com/photo-1559847844-5315695dadae?auto=format&fit=crop&w=900&q=80', N'mild');
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-2', N'sushi-ko', N'Hàu nướng mỡ hành', N'Hàu tươi nướng tại bàn, kết hợp mỡ hành và đậu phộng rang.', N'75.000 - 120.000 VND', N'https://images.unsplash.com/photo-1617806118233-18e1de247200?auto=format&fit=crop&w=900&q=80', N'mild');
INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-3', N'poi-sweet-lane', N'Chè khúc bạch nhiệt đới', N'Món tráng miệng mát lạnh phù hợp cho khách gia đình và trẻ em.', N'28.000 - 45.000 VND', N'https://images.unsplash.com/photo-1563805042-7684c019e1cb?auto=format&fit=crop&w=900&q=80', N'mild');
GO
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-4', N'poi', N'poi-snail-signature', N'en', N'Vinh Khanh Signature Snail House', N'An iconic snail and shellfish stop for first-time visitors.', N'This destination captures District 4 street dining at its most energetic, with a broad seafood menu and quick service.', N'Signature Snail House in Vinh Khanh', N'Iconic seafood and snail experience for tourists and locals.', 0, N'Minh Ánh', CAST(N'2026-03-19T09:05:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-3', N'poi', N'poi-snail-signature', N'vi', N'Quán Ốc Vĩnh Khánh Signature', N'Quán ốc đặc trưng với thực đơn đa dạng, phù hợp khách lần đầu đến khu phố.', N'Nằm giữa tuyến phố nhộn nhịp, quán ốc này đại diện cho tinh thần ẩm thực đường phố Quận 4: giá hợp lý, chế biến nhanh, vị đậm và luôn đông khách địa phương lẫn khách quốc tế.', N'Quán ốc nổi bật tại Vĩnh Khánh', N'Thưởng thức ốc, sò, nghêu chế biến đậm vị tại Quận 4.', 0, N'Minh Ánh', CAST(N'2026-03-19T09:25:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-5', N'poi', N'poi-sweet-lane', N'vi', N'Hẻm Chè Vĩnh Khánh', N'Điểm tráng miệng và món ngọt giúp cân bằng hành trình ăn uống.', N'Không gian nhỏ nhưng thân thiện, phù hợp để kết thúc tuyến tham quan ẩm thực với các món chè, sữa chua và đồ ăn vặt mát lạnh.', N'Hẻm chè Vĩnh Khánh', N'Điểm dừng món ngọt phù hợp cho gia đình và khách trẻ.', 0, N'Phạm Mỹ Linh', CAST(N'2026-03-16T07:10:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-2', N'poi', N'sushi-ko', N'en', N'BBQ Night Culinary Square', N'A lively seafood grill stop with upbeat local night-street energy.', N'BBQ Night is one of the key anchors of Vinh Khanh Food Street, bringing together fresh grilled seafood, bright urban lights, and an informal local social vibe.', N'BBQ Night at Vinh Khanh Food Street', N'Seafood grill spot for evening food tours in District 4.', 0, N'Minh Ánh', CAST(N'2026-03-18T07:05:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-1', N'poi', N'sushi-ko', N'vi', N'Nhà Hàng Sushi Ko', N'', N'Chào mừng quý khách đến với Nhà Hàng Sushi Ko. Chúng tôi tự hào là điểm đến lý tưởng cho những ai yêu thích hương vị Sushi nguyên bản từ Nhật Bản. Tại đây, mỗi món ăn đều được chế biến từ nguồn hải sản tươi ngon nhất trong ngày, kết hợp cùng kỹ nghệ điêu luyện của các nghệ nhân tâm huyết. Chúc quý khách có một trải nghiệm ẩm thực tinh tế và ấm cúng trong không gian của chúng tôi.', N'Nhà Hàng Sushi Ko', N'Nhà Hàng Sushi Ko', 0, N'Ánh Xuân', CAST(N'2026-04-05T10:24:38.6656485+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PoiTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-c267c49e', N'poi', N'sushi-ko', N'zh-CN', N'Quảng Trường Ẩm thực BBQ Night', N'', N'烧烤之夜是永庆美食街的一大亮点，游客可以在这里通过烤海鲜的香气和热闹的当地氛围，体验西贡充满活力的街头生活。', N'', N'', 1, N'Ánh Xuân', CAST(N'2026-03-21T03:10:22.6481221+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-3', N'poi', N'poi-snail-signature', N'vi', N'https://cdn.example.com/audio/snail-house-vi.mp3', N'south', N'uploaded', N'ready', N'Minh Ánh', CAST(N'2026-03-19T09:35:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-4', N'poi', N'poi-sweet-lane', N'vi', N'', N'south', N'tts', N'missing', N'Phạm Mỹ Linh', CAST(N'2026-03-16T07:05:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-2', N'poi', N'sushi-ko', N'en', N'https://cdn.example.com/audio/bbq-night-en.mp3', N'standard', N'tts', N'ready', N'Minh Ánh', CAST(N'2026-03-18T07:20:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-1', N'poi', N'sushi-ko', N'vi', N'https://cdn.example.com/audio/bbq-night-vi.mp3', N'standard', N'tts', N'ready', N'Ánh Xuân', CAST(N'2026-04-05T10:24:38.7473683+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-5f585e13', N'poi', N'sushi-ko', N'zh-CN', N'', N'south', N'tts', N'ready', N'Ánh Xuân', CAST(N'2026-03-21T03:10:22.6039699+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES (N'media-1', N'poi', N'sushi-ko', N'image', N'https://images.unsplash.com/photo-1520201163981-8cc95007dd2e?auto=format&fit=crop&w=1200&q=80', N'Không khí phố ẩm thực Vĩnh Khánh về đêm', CAST(N'2026-03-04T02:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES (N'media-2', N'poi', N'poi-snail-signature', N'image', N'https://images.unsplash.com/photo-1514933651103-005eec06c04b?auto=format&fit=crop&w=1200&q=80', N'Đĩa ốc xào phục vụ tại quán', CAST(N'2026-03-03T02:00:00.0000000+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.Routes (Id, Name, Theme, [Description], DurationMinutes, Difficulty, CoverImageUrl, IsFeatured, IsActive, UpdatedBy, UpdatedAt) VALUES (N'route-1', N'Khởi đầu 45 phút', N'Ăn vặt', N'Tour ngắn cho khách mới đến, ưu tiên các POI nổi bật và những món dễ tiếp cận.', 45, N'custom', N'', 1, 1, N'Minh Ánh', CAST(N'2026-03-21T08:00:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.Routes (Id, Name, Theme, [Description], DurationMinutes, Difficulty, CoverImageUrl, IsFeatured, IsActive, UpdatedBy, UpdatedAt) VALUES (N'route-2', N'Hải sản buổi tối', N'Hải sản', N'Tour buổi tối tập trung vào món nướng, ốc và không khí phố ẩm thực về đêm.', 70, N'custom', N'', 1, 1, N'Minh Ánh', CAST(N'2026-03-22T10:30:00.0000000+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (N'route-1', 1, N'poi-snail-signature');
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (N'route-2', 1, N'sushi-ko');
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (N'route-2', 2, N'poi-snail-signature');
GO
INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status]) VALUES (N'promo-1', N'sushi-ko', N'Combo BBQ nhóm 4 người', N'Tặng nước uống cho khách check-in POI trong khung giờ 18h-20h.', CAST(N'2026-03-18T11:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-25T14:00:00.0000000+00:00' AS datetimeoffset(7)), N'active');
INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status]) VALUES (N'promo-2', N'poi-snail-signature', N'Giảm 10% cho khách nghe audio guide', N'Áp dụng khi khách hoàn tất bài thuyết minh tiếng Việt hoặc tiếng Anh.', CAST(N'2026-03-20T10:00:00.0000000+00:00' AS datetimeoffset(7)), CAST(N'2026-03-28T14:00:00.0000000+00:00' AS datetimeoffset(7)), N'upcoming');
GO
INSERT INTO dbo.Reviews (Id, PoiId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]) VALUES (N'review-1', N'poi-snail-signature', N'Thu Trang', 5, N'Chạm vào POI trên bản đồ rồi nghe giới thiệu rất tiện và dễ hiểu.', N'vi', CAST(N'2026-03-18T05:00:00.0000000+00:00' AS datetimeoffset(7)), N'approved');
INSERT INTO dbo.Reviews (Id, PoiId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]) VALUES (N'review-2', N'sushi-ko', N'Lucas', 4, N'Useful overview before starting the food route.', N'en', CAST(N'2026-03-18T08:00:00.0000000+00:00' AS datetimeoffset(7)), N'pending');
GO
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-14', N'sushi-ko', N'en', N'ios', CAST(N'2026-03-19T09:40:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-12', N'poi-snail-signature', N'en', N'ios', CAST(N'2026-03-18T18:25:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-11', N'sushi-ko', N'vi', N'android', CAST(N'2026-03-18T12:45:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-9', N'poi-snail-signature', N'ja', N'android', CAST(N'2026-03-17T11:20:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-8', N'sushi-ko', N'en', N'ios', CAST(N'2026-03-16T19:40:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-6', N'poi-snail-signature', N'vi', N'android', CAST(N'2026-03-15T18:30:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-5', N'sushi-ko', N'zh-CN', N'ios', CAST(N'2026-03-15T13:10:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-2', N'poi-snail-signature', N'en', N'ios', CAST(N'2026-03-13T12:20:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PoiId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-1', N'sushi-ko', N'vi', N'web', CAST(N'2026-03-13T11:10:00.0000000+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-10', N'sushi-ko', N'vi', CAST(N'2026-03-19T10:15:00.0000000+00:00' AS datetimeoffset(7)), 118);
INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-8', N'poi-snail-signature', N'ja', CAST(N'2026-03-18T20:55:00.0000000+00:00' AS datetimeoffset(7)), 95);
INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-7', N'sushi-ko', N'en', CAST(N'2026-03-18T20:20:00.0000000+00:00' AS datetimeoffset(7)), 91);
INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-5', N'poi-snail-signature', N'vi', CAST(N'2026-03-16T18:15:00.0000000+00:00' AS datetimeoffset(7)), 102);
INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-4', N'sushi-ko', N'zh-CN', CAST(N'2026-03-15T19:00:00.0000000+00:00' AS datetimeoffset(7)), 110);
INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-2', N'poi-snail-signature', N'en', CAST(N'2026-03-14T12:10:00.0000000+00:00' AS datetimeoffset(7)), 88);
INSERT INTO dbo.AudioListenLogs (Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-1', N'sushi-ko', N'vi', CAST(N'2026-03-13T11:30:00.0000000+00:00' AS datetimeoffset(7)), 96);
GO
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
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-1', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật cấu hình ngôn ngữ premium', N'settings', CAST(N'2026-03-19T02:15:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3', N'Lê Quốc Bảo', N'PLACE_OWNER', N'Cập nhật mô tả món ăn đặc trưng', N'poi-snail-signature', CAST(N'2026-03-18T07:12:00.0000000+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-4', N'Ánh Xuân', N'SUPER_ADMIN', N'Khóa tài khoản chủ quán Hẻm Chè', N'user-owner-dessert', CAST(N'2026-03-16T09:00:00.0000000+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.SystemSettings (Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage, PremiumUnlockPriceUsd, MapProvider, StorageProvider, TtsProvider, GeofenceRadiusMeters, GuestReviewEnabled, AnalyticsRetentionDays) VALUES (1, N'Hệ thống quản trị thuyết minh Vĩnh Khánh', N'support@vinhkhanh.vn', N'vi', N'en', 20, N'openstreetmap', N'cloudinary', N'google_translate', 60, 1, 180);
GO
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'free', N'en');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'free', N'vi');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'premium', N'ja');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'premium', N'ko');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'premium', N'zh-CN');
GO
