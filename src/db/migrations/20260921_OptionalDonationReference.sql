SET XACT_ABORT ON;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Donation')
      AND name = N'ReferenceNumber'
      AND is_nullable = 0
)
BEGIN
    ALTER TABLE dbo.Donation
        ALTER COLUMN ReferenceNumber nvarchar(255) NULL;
END;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Donation')
      AND name = N'UX_Donation_Organization_ReferenceNumber'
      AND
      (
          has_filter = 0
          OR filter_definition IS NULL
          OR filter_definition NOT LIKE N'%ReferenceNumber%IS NOT NULL%'
      )
)
BEGIN
    DROP INDEX UX_Donation_Organization_ReferenceNumber
        ON dbo.Donation;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Donation')
      AND name = N'UX_Donation_Organization_ReferenceNumber'
)
BEGIN
    CREATE UNIQUE INDEX UX_Donation_Organization_ReferenceNumber
        ON dbo.Donation
        (
            EMAOrganizationID,
            ReferenceNumber
        )
        WHERE ReferenceNumber IS NOT NULL;
END;
GO
