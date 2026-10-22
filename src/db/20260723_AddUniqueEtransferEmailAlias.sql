/*
    Ensures that one e-transfer alias can belong to only one organization.

    The script stops before changing data when duplicates already exist.
    Resolve the returned rows, then run the script again.
*/

IF EXISTS
(
    SELECT 1
    FROM dbo.Organization
    WHERE EtransferEmailAddress IS NOT NULL
      AND LTRIM(RTRIM(EtransferEmailAddress)) <> ''
    GROUP BY LOWER(LTRIM(RTRIM(EtransferEmailAddress)))
    HAVING COUNT(*) > 1
)
BEGIN
    SELECT
        LOWER(LTRIM(RTRIM(EtransferEmailAddress)))
            AS EtransferEmailAddress,
        COUNT(*)
            AS OrganizationCount
    FROM dbo.Organization
    WHERE EtransferEmailAddress IS NOT NULL
      AND LTRIM(RTRIM(EtransferEmailAddress)) <> ''
    GROUP BY LOWER(LTRIM(RTRIM(EtransferEmailAddress)))
    HAVING COUNT(*) > 1;

    THROW 50001,
        'Duplicate e-transfer email addresses exist. Resolve them before creating the unique index.',
        1;
END;
GO

UPDATE dbo.Organization
SET EtransferEmailAddress =
    NULLIF(
        LOWER(
            LTRIM(
                RTRIM(
                    EtransferEmailAddress))),
        '')
WHERE EtransferEmailAddress IS NOT NULL;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_Organization_EtransferEmailAddress'
      AND object_id = OBJECT_ID('dbo.Organization')
)
BEGIN
    CREATE UNIQUE INDEX UX_Organization_EtransferEmailAddress
        ON dbo.Organization(EtransferEmailAddress)
        WHERE EtransferEmailAddress IS NOT NULL;
END;
GO