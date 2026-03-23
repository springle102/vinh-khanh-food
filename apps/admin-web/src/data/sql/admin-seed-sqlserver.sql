IF DB_ID(N'VinhKhanhFoodAdmin') IS NULL
BEGIN
    EXEC(N'CREATE DATABASE [VinhKhanhFoodAdmin]');
END;
GO
USE [VinhKhanhFoodAdmin];
GO

-- ============================================
-- Xóa bảng cũ nếu đã tồn tại
-- ============================================
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
IF OBJECT_ID(N'dbo.PlaceTags', N'U') IS NOT NULL
    DROP TABLE dbo.PlaceTags;
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
IF OBJECT_ID(N'dbo.QRCodes', N'U') IS NOT NULL
    DROP TABLE dbo.QRCodes;
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
IF OBJECT_ID(N'dbo.FoodItems', N'U') IS NOT NULL
    DROP TABLE dbo.FoodItems;
GO
IF OBJECT_ID(N'dbo.Places', N'U') IS NOT NULL
    DROP TABLE dbo.Places;
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
    ManagedPlaceId NVARCHAR(50) NULL
);
GO

CREATE TABLE dbo.CustomerUsers (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    Name NVARCHAR(120) NOT NULL,
    Email NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(30) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    PreferredLanguage NVARCHAR(20) NOT NULL,
    IsPremium BIT NOT NULL,
    TotalScans INT NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    LastActiveAt DATETIMEOFFSET(7) NULL
);
GO

CREATE TABLE dbo.Places (
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
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_Places_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(Id),
    CONSTRAINT FK_Places_AdminUsers FOREIGN KEY (OwnerUserId) REFERENCES dbo.AdminUsers(Id)
);
GO

CREATE TABLE dbo.CustomerFavoritePlaces (
    CustomerUserId NVARCHAR(50) NOT NULL,
    PlaceId NVARCHAR(50) NOT NULL,
    PRIMARY KEY (CustomerUserId, PlaceId),
    CONSTRAINT FK_CustomerFavoritePlaces_CustomerUsers FOREIGN KEY (CustomerUserId) REFERENCES dbo.CustomerUsers(Id),
    CONSTRAINT FK_CustomerFavoritePlaces_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)
);
GO

CREATE TABLE dbo.PlaceTags (
    PlaceId NVARCHAR(50) NOT NULL,
    TagValue NVARCHAR(100) NOT NULL,
    PRIMARY KEY (PlaceId, TagValue),
    CONSTRAINT FK_PlaceTags_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)
);
GO

CREATE TABLE dbo.FoodItems (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PlaceId NVARCHAR(50) NOT NULL,
    Name NVARCHAR(150) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    PriceRange NVARCHAR(100) NOT NULL,
    ImageUrl NVARCHAR(500) NOT NULL,
    SpicyLevel NVARCHAR(30) NOT NULL,
    CONSTRAINT FK_FoodItems_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)
);
GO

CREATE TABLE dbo.PlaceTranslations (
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
    CONSTRAINT UQ_PlaceTranslations UNIQUE (EntityType, EntityId, LanguageCode)
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

CREATE TABLE dbo.QRCodes (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    EntityType NVARCHAR(30) NOT NULL,
    EntityId NVARCHAR(50) NOT NULL,
    QrValue NVARCHAR(500) NOT NULL,
    QrImageUrl NVARCHAR(500) NOT NULL,
    IsActive BIT NOT NULL,
    LastScanAt DATETIMEOFFSET(7) NULL
);
GO

CREATE TABLE dbo.Routes (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    DurationMinutes INT NOT NULL,
    Difficulty NVARCHAR(30) NOT NULL,
    IsFeatured BIT NOT NULL
);
GO

CREATE TABLE dbo.RouteStops (
    RouteId NVARCHAR(50) NOT NULL,
    StopOrder INT NOT NULL,
    PlaceId NVARCHAR(50) NOT NULL,
    PRIMARY KEY (RouteId, StopOrder),
    CONSTRAINT FK_RouteStops_Routes FOREIGN KEY (RouteId) REFERENCES dbo.Routes(Id),
    CONSTRAINT FK_RouteStops_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)
);
GO

CREATE TABLE dbo.Promotions (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PlaceId NVARCHAR(50) NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    StartAt DATETIMEOFFSET(7) NOT NULL,
    EndAt DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    CONSTRAINT FK_Promotions_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)
);
GO

CREATE TABLE dbo.Reviews (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PlaceId NVARCHAR(50) NOT NULL,
    UserName NVARCHAR(120) NOT NULL,
    Rating INT NOT NULL,
    CommentText NVARCHAR(MAX) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    [Status] NVARCHAR(30) NOT NULL,
    CONSTRAINT FK_Reviews_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id),
    CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5)
);
GO

CREATE TABLE dbo.ViewLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PlaceId NVARCHAR(50) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    DeviceType NVARCHAR(20) NOT NULL,
    ViewedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_ViewLogs_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)
);
GO

CREATE TABLE dbo.AudioListenLogs (
    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
    PlaceId NVARCHAR(50) NOT NULL,
    LanguageCode NVARCHAR(20) NOT NULL,
    ListenedAt DATETIMEOFFSET(7) NOT NULL,
    DurationInSeconds INT NOT NULL,
    CONSTRAINT FK_AudioListenLogs_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)
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
    QrAutoPlay BIT NOT NULL,
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
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-oc', N'Ốc & Hải sản', N'oc-hai-san', N'shell', N'#f97316');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-bbq', N'Nướng & Beer', N'nuong-beer', N'flame', N'#de6245');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-dessert', N'Tráng miệng', N'trang-mieng', N'sparkle', N'#d9a845');
INSERT INTO dbo.Categories (Id, Name, Slug, Icon, Color) VALUES (N'cat-culture', N'Điểm văn hóa', N'diem-van-hoa', N'landmark', N'#9a3412');
GO
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPlaceId) VALUES (N'user-super', N'Ánh Xuân', N'superadmin@vinhkhanh.vn', N'0909 188 001', N'SUPER_ADMIN', N'Admin@123', N'active', CAST(N'2025-11-19T02:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-23T15:18:50.2644859+00:00' AS datetimeoffset(7)), N'#f97316', NULL);
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPlaceId) VALUES (N'user-owner-bbq', N'Minh Ánh', N'bbq@vinhkhanh.vn', N'0909 188 002', N'PLACE_OWNER', N'Admin@123', N'active', CAST(N'2025-12-14T02:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-21T11:21:10.8821655+00:00' AS datetimeoffset(7)), N'#de6245', N'place-bbq-night');
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPlaceId) VALUES (N'user-owner-oc', N'Lê Quốc Bảo', N'oc@vinhkhanh.vn', N'0909 188 003', N'PLACE_OWNER', N'Admin@123', N'active', CAST(N'2025-12-29T02:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-18T11:22:00+00:00' AS datetimeoffset(7)), N'#d9a845', N'place-snail-signature');
INSERT INTO dbo.AdminUsers (Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPlaceId) VALUES (N'user-owner-dessert', N'Phạm Mỹ Linh', N'che@vinhkhanh.vn', N'0909 188 004', N'PLACE_OWNER', N'Admin@123', N'locked', CAST(N'2026-01-05T02:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-12T07:20:00+00:00' AS datetimeoffset(7)), N'#7c2d12', N'place-sweet-lane');
GO
INSERT INTO dbo.CustomerUsers (Id, Name, Email, Phone, [Status], PreferredLanguage, IsPremium, TotalScans, CreatedAt, LastActiveAt) VALUES (N'customer-1', N'Nguyễn Bảo Vy', N'baovy@gmail.com', N'0911 000 111', N'active', N'vi', 0, 12, CAST(N'2026-01-10T03:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-19T03:15:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.CustomerUsers (Id, Name, Email, Phone, [Status], PreferredLanguage, IsPremium, TotalScans, CreatedAt, LastActiveAt) VALUES (N'customer-2', N'Lucas Martin', N'lucas@example.com', N'+84 901 111 222', N'active', N'en', 1, 24, CAST(N'2026-01-15T03:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-19T05:20:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.CustomerUsers (Id, Name, Email, Phone, [Status], PreferredLanguage, IsPremium, TotalScans, CreatedAt, LastActiveAt) VALUES (N'customer-3', N'Kim Seo Yoon', N'seoyoon@example.com', N'+84 902 222 333', N'blocked', N'ko', 1, 8, CAST(N'2026-02-02T03:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-17T09:05:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.Places (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, DefaultLanguageCode, District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt) VALUES (N'place-bbq-night', N'quang-truong-am-thuc-bbq-night', N'126 Vĩnh Khánh, Phường Khánh Hội, TP.HCM', 10.763724, 106.701693, N'cat-bbq', N'published', 1, N'vi', N'Quận 4', N'Khánh Hội', N'120.000 - 350.000 VND', 50, 96, N'user-owner-bbq', N'Ánh Xuân', CAST(N'2025-11-19T03:15:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-23T15:09:14.6740258+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.Places (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, DefaultLanguageCode, District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt) VALUES (N'place-snail-signature', N'quan-oc-vinh-khanh-signature', N'42 Vĩnh Khánh, Phường Khánh Hội, TP.HCM', 10.75803, 106.70162, N'cat-oc', N'published', 1, N'vi', N'Quận 4', N'Khánh Hội', N'80.000 - 280.000 VND', 45, 98, N'user-owner-oc', N'Minh Ánh', CAST(N'2025-11-19T02:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-19T09:25:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.Places (Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, DefaultLanguageCode, District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt) VALUES (N'place-sweet-lane', N'hem-che-vinh-khanh', N'88/4 Vĩnh Khánh, Phường Vĩnh Hội, TP.HCM', 10.75712, 106.70302, N'cat-dessert', N'draft', 0, N'vi', N'Quận 4', N'Vĩnh Hội', N'25.000 - 75.000 VND', 25, 73, N'user-owner-dessert', N'Phạm Mỹ Linh', CAST(N'2026-01-16T02:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-16T07:10:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.CustomerFavoritePlaces (CustomerUserId, PlaceId) VALUES (N'customer-1', N'place-snail-signature');
INSERT INTO dbo.CustomerFavoritePlaces (CustomerUserId, PlaceId) VALUES (N'customer-1', N'place-bbq-night');
INSERT INTO dbo.CustomerFavoritePlaces (CustomerUserId, PlaceId) VALUES (N'customer-2', N'place-bbq-night');
INSERT INTO dbo.CustomerFavoritePlaces (CustomerUserId, PlaceId) VALUES (N'customer-3', N'place-snail-signature');
GO
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-bbq-night', N'hải sản nướng');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-bbq-night', N'nhóm bạn');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-bbq-night', N'ban đêm');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-snail-signature', N'ốc');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-snail-signature', N'đặc sản');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-snail-signature', N'gia đình');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-sweet-lane', N'chè');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-sweet-lane', N'ăn vặt');
INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (N'place-sweet-lane', N'gia đình');
GO
INSERT INTO dbo.FoodItems (Id, PlaceId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-1', N'place-snail-signature', N'Ốc hương xào bơ tỏi', N'Món bán chạy với sốt bơ tỏi đậm vị, thơm và ngậy.', N'95.000 - 140.000 VND', N'https://images.unsplash.com/photo-1559847844-5315695dadae?auto=format&fit=crop&w=900&q=80', N'mild');
INSERT INTO dbo.FoodItems (Id, PlaceId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-2', N'place-bbq-night', N'Hàu nướng mỡ hành', N'Hàu tươi nướng tại bàn, kết hợp mỡ hành và đậu phộng rang.', N'75.000 - 120.000 VND', N'https://images.unsplash.com/photo-1617806118233-18e1de247200?auto=format&fit=crop&w=900&q=80', N'mild');
INSERT INTO dbo.FoodItems (Id, PlaceId, Name, [Description], PriceRange, ImageUrl, SpicyLevel) VALUES (N'food-3', N'place-sweet-lane', N'Chè khúc bạch nhiệt đới', N'Món tráng miệng mát lạnh phù hợp cho khách gia đình và trẻ em.', N'28.000 - 45.000 VND', N'https://images.unsplash.com/photo-1563805042-7684c019e1cb?auto=format&fit=crop&w=900&q=80', N'mild');
GO
INSERT INTO dbo.PlaceTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-c267c49e', N'place', N'place-bbq-night', N'zh-CN', N'Quảng Trường Ẩm thực BBQ Night', N'', N'烧烤之夜是永庆美食街的一大亮点，游客可以在这里通过烤海鲜的香气和热闹的当地氛围，体验西贡充满活力的街头生活。', N'', N'', 1, N'Ánh Xuân', CAST(N'2026-03-21T03:10:22.6481221+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PlaceTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-1', N'place', N'place-bbq-night', N'vi', N'Quảng trường Ẩm thực BBQ Night', N'Điểm tụ họp sôi động với món nướng hải sản và không khí phố đêm náo nhiệt.', N'BBQ Night là điểm dừng chân nổi bật của Phố ẩm thực Vĩnh Khánh, nơi du khách có thể cảm nhận nhịp sống đường phố Sài Gòn qua hương thơm của hải sản nướng và không khí tụ hội đậm chất địa phương.', N'BBQ Night - Phố Ẩm thực Vĩnh Khánh', N'Điểm nướng hải sản nổi bật cho nhóm bạn và khách du lịch về đêm.', 0, N'Ánh Xuân', CAST(N'2026-03-23T15:09:14.7035213+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PlaceTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-2', N'place', N'place-bbq-night', N'en', N'BBQ Night Culinary Square', N'A lively seafood grill stop with upbeat local night-street energy.', N'BBQ Night is one of the key anchors of Vinh Khanh Food Street, bringing together fresh grilled seafood, bright urban lights, and an informal local social vibe.', N'BBQ Night at Vinh Khanh Food Street', N'Seafood grill spot for evening food tours in District 4.', 0, N'Minh Ánh', CAST(N'2026-03-18T07:05:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PlaceTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-3', N'place', N'place-snail-signature', N'vi', N'Quán Ốc Vĩnh Khánh Signature', N'Quán ốc đặc trưng với thực đơn đa dạng, phù hợp khách lần đầu đến khu phố.', N'Nằm giữa tuyến phố nhộn nhịp, quán ốc này đại diện cho tinh thần ẩm thực đường phố Quận 4: giá hợp lý, chế biến nhanh, vị đậm và luôn đông khách địa phương lẫn khách quốc tế.', N'Quán ốc nổi bật tại Vĩnh Khánh', N'Thưởng thức ốc, sò, nghêu chế biến đậm vị tại Quận 4.', 0, N'Minh Ánh', CAST(N'2026-03-19T09:25:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PlaceTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-4', N'place', N'place-snail-signature', N'en', N'Vinh Khanh Signature Snail House', N'An iconic snail and shellfish stop for first-time visitors.', N'This destination captures District 4 street dining at its most energetic, with a broad seafood menu and quick service.', N'Signature Snail House in Vinh Khanh', N'Iconic seafood and snail experience for tourists and locals.', 0, N'Minh Ánh', CAST(N'2026-03-19T09:05:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.PlaceTranslations (Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt) VALUES (N'trans-5', N'place', N'place-sweet-lane', N'vi', N'Hẻm Chè Vĩnh Khánh', N'Điểm tráng miệng và món ngọt giúp cân bằng hành trình ăn uống.', N'Không gian nhỏ nhưng thân thiện, phù hợp để kết thúc tuyến tham quan ẩm thực với các món chè, sữa chua và đồ ăn vặt mát lạnh.', N'Hẻm chè Vĩnh Khánh', N'Điểm dừng món ngọt phù hợp cho gia đình và khách trẻ.', 0, N'Phạm Mỹ Linh', CAST(N'2026-03-16T07:10:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-5f585e13', N'place', N'place-bbq-night', N'zh-CN', N'', N'south', N'tts', N'ready', N'Ánh Xuân', CAST(N'2026-03-21T03:10:22.6039699+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-1', N'place', N'place-bbq-night', N'vi', N'https://cdn.example.com/audio/bbq-night-vi.mp3', N'standard', N'tts', N'ready', N'Ánh Xuân', CAST(N'2026-03-23T15:09:14.7659864+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-2', N'place', N'place-bbq-night', N'en', N'https://cdn.example.com/audio/bbq-night-en.mp3', N'standard', N'tts', N'ready', N'Minh Ánh', CAST(N'2026-03-18T07:20:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-3', N'place', N'place-snail-signature', N'vi', N'https://cdn.example.com/audio/snail-house-vi.mp3', N'south', N'uploaded', N'ready', N'Minh Ánh', CAST(N'2026-03-19T09:35:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AudioGuides (Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt) VALUES (N'audio-4', N'place', N'place-sweet-lane', N'vi', N'', N'south', N'tts', N'missing', N'Phạm Mỹ Linh', CAST(N'2026-03-16T07:05:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES (N'media-1', N'place', N'place-bbq-night', N'image', N'https://images.unsplash.com/photo-1520201163981-8cc95007dd2e?auto=format&fit=crop&w=1200&q=80', N'Không khí phố ẩm thực Vĩnh Khánh về đêm', CAST(N'2026-03-04T02:00:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt) VALUES (N'media-2', N'place', N'place-snail-signature', N'image', N'https://images.unsplash.com/photo-1514933651103-005eec06c04b?auto=format&fit=crop&w=1200&q=80', N'Đĩa ốc xào phục vụ tại quán', CAST(N'2026-03-03T02:00:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.QRCodes (Id, EntityType, EntityId, QrValue, QrImageUrl, IsActive, LastScanAt) VALUES (N'qr-1', N'place', N'place-bbq-night', N'https://guide.vinhkhanh.vn/scan/quang-truong-am-thuc-bbq-night', N'https://api.qrserver.com/v1/create-qr-code/?size=320x320&data=https%3A%2F%2Fguide.vinhkhanh.vn%2Fscan%2Fquang-truong-am-thuc-bbq-night', 1, CAST(N'2026-03-19T12:10:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.QRCodes (Id, EntityType, EntityId, QrValue, QrImageUrl, IsActive, LastScanAt) VALUES (N'qr-2', N'place', N'place-snail-signature', N'https://guide.vinhkhanh.vn/scan/quan-oc-vinh-khanh-signature', N'', 1, CAST(N'2026-03-18T12:15:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.QRCodes (Id, EntityType, EntityId, QrValue, QrImageUrl, IsActive, LastScanAt) VALUES (N'qr-3', N'place', N'place-sweet-lane', N'https://guide.vinhkhanh.vn/scan/hem-che-vinh-khanh', N'', 0, CAST(N'2026-03-17T12:20:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.Routes (Id, Name, [Description], DurationMinutes, Difficulty, IsFeatured) VALUES (N'route-1', N'Khởi đầu 45 phút', N'Lộ trình ngắn cho khách mới đến, ưu tiên QR, cổng thông tin và 2 quán nổi bật.', 45, N'easy', 1);
INSERT INTO dbo.Routes (Id, Name, [Description], DurationMinutes, Difficulty, IsFeatured) VALUES (N'route-2', N'Hải sản buổi tối', N'Tuyến ăn tối thiên về món nướng, ốc và không khí đường phố về đêm.', 70, N'foodie', 1);
GO
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PlaceId) VALUES (N'route-1', 1, N'place-snail-signature');
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PlaceId) VALUES (N'route-2', 1, N'place-bbq-night');
INSERT INTO dbo.RouteStops (RouteId, StopOrder, PlaceId) VALUES (N'route-2', 2, N'place-snail-signature');
GO
INSERT INTO dbo.Promotions (Id, PlaceId, Title, [Description], StartAt, EndAt, [Status]) VALUES (N'promo-1', N'place-bbq-night', N'Combo BBQ nhóm 4 người', N'Tặng nước uống khi quét QR trong khung giờ 18h-20h.', CAST(N'2026-03-18T11:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-25T14:00:00+00:00' AS datetimeoffset(7)), N'active');
INSERT INTO dbo.Promotions (Id, PlaceId, Title, [Description], StartAt, EndAt, [Status]) VALUES (N'promo-2', N'place-snail-signature', N'Giảm 10% cho khách nghe audio guide', N'Áp dụng khi khách hoàn tất bài thuyết minh tiếng Việt hoặc tiếng Anh.', CAST(N'2026-03-20T10:00:00+00:00' AS datetimeoffset(7)), CAST(N'2026-03-28T14:00:00+00:00' AS datetimeoffset(7)), N'upcoming');
GO
INSERT INTO dbo.Reviews (Id, PlaceId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]) VALUES (N'review-1', N'place-snail-signature', N'Thu Trang', 5, N'Quét QR xong nghe giới thiệu rất tiện và dễ hiểu.', N'vi', CAST(N'2026-03-18T05:00:00+00:00' AS datetimeoffset(7)), N'approved');
INSERT INTO dbo.Reviews (Id, PlaceId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]) VALUES (N'review-2', N'place-bbq-night', N'Lucas', 4, N'Useful overview before starting the food route.', N'en', CAST(N'2026-03-18T08:00:00+00:00' AS datetimeoffset(7)), N'pending');
GO
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-1', N'place-bbq-night', N'vi', N'web', CAST(N'2026-03-13T11:10:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-2', N'place-snail-signature', N'en', N'ios', CAST(N'2026-03-13T12:20:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-5', N'place-bbq-night', N'zh-CN', N'ios', CAST(N'2026-03-15T13:10:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-6', N'place-snail-signature', N'vi', N'android', CAST(N'2026-03-15T18:30:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-8', N'place-bbq-night', N'en', N'ios', CAST(N'2026-03-16T19:40:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-9', N'place-snail-signature', N'ja', N'android', CAST(N'2026-03-17T11:20:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-11', N'place-bbq-night', N'vi', N'android', CAST(N'2026-03-18T12:45:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-12', N'place-snail-signature', N'en', N'ios', CAST(N'2026-03-18T18:25:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.ViewLogs (Id, PlaceId, LanguageCode, DeviceType, ViewedAt) VALUES (N'view-14', N'place-bbq-night', N'en', N'ios', CAST(N'2026-03-19T09:40:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.AudioListenLogs (Id, PlaceId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-1', N'place-bbq-night', N'vi', CAST(N'2026-03-13T11:30:00+00:00' AS datetimeoffset(7)), 96);
INSERT INTO dbo.AudioListenLogs (Id, PlaceId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-2', N'place-snail-signature', N'en', CAST(N'2026-03-14T12:10:00+00:00' AS datetimeoffset(7)), 88);
INSERT INTO dbo.AudioListenLogs (Id, PlaceId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-4', N'place-bbq-night', N'zh-CN', CAST(N'2026-03-15T19:00:00+00:00' AS datetimeoffset(7)), 110);
INSERT INTO dbo.AudioListenLogs (Id, PlaceId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-5', N'place-snail-signature', N'vi', CAST(N'2026-03-16T18:15:00+00:00' AS datetimeoffset(7)), 102);
INSERT INTO dbo.AudioListenLogs (Id, PlaceId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-7', N'place-bbq-night', N'en', CAST(N'2026-03-18T20:20:00+00:00' AS datetimeoffset(7)), 91);
INSERT INTO dbo.AudioListenLogs (Id, PlaceId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-8', N'place-snail-signature', N'ja', CAST(N'2026-03-18T20:55:00+00:00' AS datetimeoffset(7)), 95);
INSERT INTO dbo.AudioListenLogs (Id, PlaceId, LanguageCode, ListenedAt, DurationInSeconds) VALUES (N'listen-10', N'place-bbq-night', N'vi', CAST(N'2026-03-19T10:15:00+00:00' AS datetimeoffset(7)), 118);
GO
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-bd53c6fe', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:18:50.2650712+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-9a4656f9', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:14:27.3125511+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-b368a6a2', N'Ánh Xuân', N'SYSTEM', N'Cap nhat audio guide', N'place:vi:place-bbq-night', CAST(N'2026-03-23T15:09:14.7662388+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-465e19bf', N'Ánh Xuân', N'SYSTEM', N'Cap nhat noi dung thuyet minh', N'place:vi:place-bbq-night', CAST(N'2026-03-23T15:09:14.7036302+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-4d658113', N'Ánh Xuân', N'SYSTEM', N'Cap nhat dia diem', N'quang-truong-am-thuc-bbq-night', CAST(N'2026-03-23T15:09:14.674846+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-de17906e', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:07:17.7228126+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-f5e84580', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T15:00:26.0584588+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2c6459c5', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-23T14:42:46.2012395+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-45963728', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-22T08:15:30.6439716+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2dd6d649', N'Minh Ánh', N'PLACE_OWNER', N'Dang nhap admin', N'bbq@vinhkhanh.vn', CAST(N'2026-03-21T11:21:10.8821922+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2700d5bc', N'Ánh Xuân', N'SUPER_ADMIN', N'Cap nhat tai khoan admin', N'bbq@vinhkhanh.vn', CAST(N'2026-03-21T11:18:35.6725529+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-62561041', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T11:17:36.073975+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-fc2031e1', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T11:08:06.6183748+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3ee8b781', N'Ánh Xuân', N'SYSTEM', N'Tao noi dung thuyet minh', N'place:zh-CN:place-bbq-night', CAST(N'2026-03-21T03:10:22.6482714+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-58bd5af3', N'Ánh Xuân', N'SYSTEM', N'Tao audio guide', N'place:zh-CN:place-bbq-night', CAST(N'2026-03-21T03:10:22.6043302+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-fcb65e4f', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T03:01:37.8873244+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-dd447361', N'Minh Ánh', N'SYSTEM', N'Cap nhat noi dung thuyet minh', N'place:vi:place-bbq-night', CAST(N'2026-03-21T02:26:09.7445932+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-a967b25e', N'Minh Ánh', N'SYSTEM', N'Cap nhat dia diem', N'quang-truong-am-thuc-bbq-night', CAST(N'2026-03-21T02:26:09.7190989+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2843c756', N'Minh Ánh', N'PLACE_OWNER', N'Cap nhat trang thai QR', N'qr-1', CAST(N'2026-03-21T02:22:30.5852833+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3dd11211', N'Minh Ánh', N'PLACE_OWNER', N'Cap nhat trang thai QR', N'qr-1', CAST(N'2026-03-21T02:22:29.7042164+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-b259ec20', N'Minh Ánh', N'PLACE_OWNER', N'Cap nhat trang thai danh gia', N'review-1', CAST(N'2026-03-21T02:21:53.3383987+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2f58eea4', N'Minh Ánh', N'PLACE_OWNER', N'Cap nhat trang thai danh gia', N'review-1', CAST(N'2026-03-21T02:21:51.5003326+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-23a11658', N'Minh Ánh', N'PLACE_OWNER', N'Cap nhat trang thai danh gia', N'review-1', CAST(N'2026-03-21T02:21:49.9912229+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-44134089', N'Minh Ánh', N'PLACE_OWNER', N'Cap nhat trang thai danh gia', N'review-1', CAST(N'2026-03-21T02:21:49.1313145+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-a20b6180', N'Minh Ánh', N'PLACE_OWNER', N'Cap nhat trang thai danh gia', N'review-1', CAST(N'2026-03-21T02:21:47.0998968+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-10d614bd', N'Minh Ánh', N'PLACE_OWNER', N'Dang nhap admin', N'content@vinhkhanh.vn', CAST(N'2026-03-21T02:19:14.7087424+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-e5f07e84', N'Ánh Xuân', N'SYSTEM', N'Cap nhat noi dung thuyet minh', N'place:vi:place-bbq-night', CAST(N'2026-03-21T01:12:11.7750195+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-10db7490', N'Ánh Xuân', N'SYSTEM', N'Cap nhat audio guide', N'place:vi:place-bbq-night', CAST(N'2026-03-21T01:12:11.7270186+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-55ebf34f', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T01:01:32.6315235+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-952ca80d', N'Ánh Xuân', N'SYSTEM', N'Cap nhat noi dung thuyet minh', N'place:vi:place-bbq-night', CAST(N'2026-03-21T00:53:15.4587231+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-43ec5465', N'Ánh Xuân', N'SYSTEM', N'Cap nhat audio guide', N'place:vi:place-bbq-night', CAST(N'2026-03-21T00:53:15.4148852+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-e8f13b8a', N'Ánh Xuân', N'SUPER_ADMIN', N'Cap nhat trang thai QR', N'qr-1', CAST(N'2026-03-21T00:52:25.1981158+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-6e4e51c3', N'Ánh Xuân', N'SUPER_ADMIN', N'Cap nhat trang thai QR', N'qr-1', CAST(N'2026-03-21T00:52:24.7414296+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-edc381f6', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T00:48:22.0356057+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-0042017d', N'Ánh Xuân', N'SUPER_ADMIN', N'Dang nhap admin', N'superadmin@vinhkhanh.vn', CAST(N'2026-03-21T00:03:59.7290246+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-1', N'Ánh Xuân', N'SUPER_ADMIN', N'Cập nhật cấu hình ngôn ngữ premium', N'settings', CAST(N'2026-03-19T02:15:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-2', N'Minh Ánh', N'PLACE_OWNER', N'Cập nhật audio tiếng Anh cho BBQ Night', N'place-bbq-night', CAST(N'2026-03-19T03:05:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-3', N'Lê Quốc Bảo', N'PLACE_OWNER', N'Cập nhật mô tả món ăn đặc trưng', N'place-snail-signature', CAST(N'2026-03-18T07:12:00+00:00' AS datetimeoffset(7)));
INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt) VALUES (N'audit-4', N'Ánh Xuân', N'SUPER_ADMIN', N'Khóa tài khoản chủ quán Hẻm Chè', N'user-owner-dessert', CAST(N'2026-03-16T09:00:00+00:00' AS datetimeoffset(7)));
GO
INSERT INTO dbo.SystemSettings (Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage, PremiumUnlockPriceUsd, MapProvider, StorageProvider, TtsProvider, GeofenceRadiusMeters, QrAutoPlay, GuestReviewEnabled, AnalyticsRetentionDays) VALUES (1, N'Hệ thống quản trị thuyết minh Vĩnh Khánh', N'support@vinhkhanh.vn', N'vi', N'en', 20, N'openstreetmap', N'cloudinary', N'azure', 60, 1, 1, 180);
GO
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'free', N'vi');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'free', N'en');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'premium', N'zh-CN');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'premium', N'ko');
INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, N'premium', N'ja');
GO

-- Gợi ý học SQL:
-- SELECT TOP 10 * FROM dbo.Places;
-- SELECT p.Id, t.LanguageCode, t.Title FROM dbo.Places p JOIN dbo.PlaceTranslations t ON p.Id = t.EntityId;
-- SELECT p.Slug, a.LanguageCode, a.SourceType FROM dbo.Places p LEFT JOIN dbo.AudioGuides a ON p.Id = a.EntityId;
