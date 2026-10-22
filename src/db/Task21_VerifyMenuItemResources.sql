/* READ-ONLY Task 21 verification. */
SET NOCOUNT ON;

SELECT
    DB_NAME() AS CurrentDatabase,
    OBJECT_ID(N'dbo.MenuItemResourceType', N'U') AS MenuItemResourceTypeObjectID,
    OBJECT_ID(N'dbo.MenuItemResource', N'U') AS MenuItemResourceObjectID,
    COL_LENGTH(N'dbo.MenuItemResource', N'AudioScript') AS AudioScriptColumnLength;

IF OBJECT_ID(N'dbo.MenuItemResourceType', N'U') IS NOT NULL
BEGIN
    SELECT
        MenuItemResourceTypeID,
        Description,
        Name,
        LEN(RawSVG) AS RawSVGLength
    FROM dbo.MenuItemResourceType
    ORDER BY MenuItemResourceTypeID;
END;

IF OBJECT_ID(N'dbo.MenuItemResource', N'U') IS NOT NULL
BEGIN
    SELECT
        mir.MenuItemRowID,
        mir.MenuItemID,
        mir.MenuItemResourceTypeID,
        mir.Sequence,
        mir.Description,
        mir.AudioScript,
        mir.AudioVideoBlob,
        mir.ScreenURL
    FROM dbo.MenuItemResource mir
    ORDER BY
        mir.MenuItemID,
        mir.Sequence,
        mir.MenuItemRowID;
END;
