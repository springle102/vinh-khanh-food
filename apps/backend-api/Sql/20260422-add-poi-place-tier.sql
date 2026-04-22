IF COL_LENGTH(N'dbo.Pois', N'PlaceTier') IS NULL
BEGIN
    ALTER TABLE dbo.Pois
    ADD PlaceTier INT NULL;
END;
GO

IF COL_LENGTH(N'dbo.Pois', N'PlaceTier') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.default_constraints defaultConstraint
       INNER JOIN sys.columns columnInfo
           ON columnInfo.object_id = defaultConstraint.parent_object_id
          AND columnInfo.column_id = defaultConstraint.parent_column_id
       WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
         AND columnInfo.name = N'PlaceTier'
   )
BEGIN
    ALTER TABLE dbo.Pois
    ADD CONSTRAINT DF_Pois_PlaceTier DEFAULT ((0)) FOR PlaceTier;
END;
GO

UPDATE dbo.Pois
SET PlaceTier = CASE
    WHEN PlaceTier = 1 THEN 1
    ELSE 0
END
WHERE PlaceTier IS NULL
   OR PlaceTier NOT IN (0, 1);
GO

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Pois')
      AND name = N'PlaceTier'
      AND is_nullable = 1
)
BEGIN
    ALTER TABLE dbo.Pois
    ALTER COLUMN PlaceTier INT NOT NULL;
END;
GO
