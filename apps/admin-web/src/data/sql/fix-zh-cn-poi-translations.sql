UPDATE dbo.PoiTranslations
SET
    Title = N'烧烤之夜美食广场',
    UpdatedBy = N'codex-unicode-fix',
    UpdatedAt = SYSUTCDATETIME()
WHERE EntityType = N'poi'
  AND EntityId = N'sushi-ko'
  AND LanguageCode = N'zh-CN'
  AND (Title = N'Quảng Trường Ẩm thực BBQ Night' OR Title IS NULL OR LTRIM(RTRIM(Title)) = N'');
