USE [VinhKhanhFoodAdmin];
GO

IF COL_LENGTH(N'dbo.Pois', N'QrCode') IS NULL
    ALTER TABLE dbo.Pois ADD QrCode NVARCHAR(150) NULL;
GO

IF COL_LENGTH(N'dbo.Pois', N'OpeningHours') IS NULL
    ALTER TABLE dbo.Pois ADD OpeningHours NVARCHAR(150) NULL;
GO

UPDATE dbo.Pois
SET QrCode = Slug
WHERE ISNULL(LTRIM(RTRIM(QrCode)), N'') = N'';
GO

UPDATE dbo.Pois
SET OpeningHours = CASE
    WHEN Id = N'poi-bbq-night' THEN N'17:00 - 23:45'
    WHEN Id = N'poi-snail-signature' THEN N'16:30 - 23:30'
    WHEN Id = N'poi-sweet-lane' THEN N'15:00 - 22:30'
    ELSE N'17:00 - 23:00'
END
WHERE ISNULL(LTRIM(RTRIM(OpeningHours)), N'') = N'';
GO

-- Nearby query sample for analytics/debug
DECLARE @lat FLOAT = 10.759300;
DECLARE @lng FLOAT = 106.701700;
DECLARE @radiusMeters FLOAT = 120;

SELECT
    p.Id,
    p.Slug,
    p.AddressLine,
    p.Latitude,
    p.Longitude,
    p.QrCode,
    p.OpeningHours
FROM dbo.Pois p
WHERE p.Status = N'published'
  AND geography::Point(@lat, @lng, 4326).STDistance(geography::Point(p.Latitude, p.Longitude, 4326)) <= @radiusMeters
ORDER BY geography::Point(@lat, @lng, 4326).STDistance(geography::Point(p.Latitude, p.Longitude, 4326));
GO
