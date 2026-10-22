/*
    Task 40.2 - Landing Page Design
    READ-ONLY verifier. SELECTs only.

    Expected configuration:
    - MenuItemRowID 0 = Landing Page
    - MenuItemTypeID 0 = Landing
    - landing selection is driven by DisplayControlID = 'L'
    - exactly one active L row per website category (10,20,30,90)
    - dedicated landing rows use Row 0 / Type 0
    - Task 40.2 temporary routes already present in dev are:
        /pub20260906
        /org20260906
        /don20260906
        /ins20260906

    No INSERT / UPDATE / DELETE / CREATE / ALTER statements are present.
*/

SET NOCOUNT ON;

PRINT '===== LOOKUP VALUES =====';
SELECT MenuItemRowID, Description
FROM dbo.MenuItemRow
WHERE MenuItemRowID = 0;

SELECT MenuItemTypeID, Description
FROM dbo.MenuItemType
WHERE MenuItemTypeID = 0;

PRINT '===== CATEGORY POINTERS (LEGACY / INFORMATIONAL) =====';
SELECT
    MenuItemCategoryID,
    Description,
    LandingPageMenuItemID
FROM dbo.MenuItemCategory
WHERE MenuItemCategoryID IN (10,20,30,90)
ORDER BY MenuItemCategoryID;

PRINT '===== ALL L ROWS =====';
SELECT
    mi.MenuItemID,
    mi.MenuItemCategoryID,
    mic.Description AS CategoryName,
    mi.MenuItemRowID,
    mi.MenuItemTypeID,
    mi.DisplayControlID,
    mi.MenuName,
    mi.ScreenTitle,
    mi.URL,
    mi.IsActive
FROM dbo.MenuItem mi
INNER JOIN dbo.MenuItemCategory mic
    ON mic.MenuItemCategoryID = mi.MenuItemCategoryID
WHERE mi.MenuItemCategoryID IN (10,20,30,90)
  AND UPPER(LTRIM(RTRIM(mi.DisplayControlID))) = 'L'
ORDER BY mi.MenuItemCategoryID, mi.MenuItemRowID, mi.MenuItemID;

PRINT '===== DEDICATED ROW-0 / TYPE-0 LANDING RECORDS =====';
SELECT
    mi.MenuItemID,
    mi.MenuItemCategoryID,
    mic.Description AS CategoryName,
    mi.MenuItemRowID,
    mi.MenuItemTypeID,
    mi.DisplayControlID,
    mi.MenuName,
    mi.ScreenTitle,
    mi.URL,
    mi.IsActive
FROM dbo.MenuItem mi
INNER JOIN dbo.MenuItemCategory mic
    ON mic.MenuItemCategoryID = mi.MenuItemCategoryID
WHERE mi.MenuItemCategoryID IN (10,20,30,90)
  AND mi.MenuItemRowID = 0
  AND mi.MenuItemTypeID = 0
ORDER BY mi.MenuItemCategoryID, mi.MenuItemID;

PRINT '===== ACTIVE L COUNT: EXPECT EXACTLY 1 PER CATEGORY =====';
SELECT
    c.MenuItemCategoryID,
    c.Description AS CategoryName,
    COUNT(mi.MenuItemID) AS ActiveLandingPageCount
FROM dbo.MenuItemCategory c
LEFT JOIN dbo.MenuItem mi
    ON mi.MenuItemCategoryID = c.MenuItemCategoryID
   AND mi.IsActive = 'Y'
   AND UPPER(LTRIM(RTRIM(mi.DisplayControlID))) = 'L'
WHERE c.MenuItemCategoryID IN (10,20,30,90)
GROUP BY c.MenuItemCategoryID, c.Description
ORDER BY c.MenuItemCategoryID;

PRINT '===== TASK 40.2 TEMPORARY ROUTE RECORDS =====';
SELECT
    mi.MenuItemID,
    mi.MenuItemCategoryID,
    mi.MenuItemRowID,
    mi.MenuItemTypeID,
    mi.DisplayControlID,
    mi.MenuName,
    mi.ScreenTitle,
    mi.URL,
    mi.IsActive
FROM dbo.MenuItem mi
WHERE LOWER(
        CASE
            WHEN LEFT(LTRIM(RTRIM(COALESCE(mi.URL, ''))), 1) = '/'
                THEN LTRIM(RTRIM(mi.URL))
            ELSE '/' + LTRIM(RTRIM(COALESCE(mi.URL, '')))
        END
      ) IN ('/pub20260906','/org20260906','/don20260906','/ins20260906')
ORDER BY mi.MenuItemCategoryID, mi.MenuItemID;

PRINT '===== ROLE MAPPINGS FOR DEDICATED LANDING RECORDS =====';
SELECT
    mi.MenuItemID,
    mi.MenuItemCategoryID,
    mi.URL,
    rmi.RoleID,
    r.RoleName,
    r.IsActive AS RoleIsActive
FROM dbo.MenuItem mi
LEFT JOIN dbo.RoleMenuItem rmi
    ON rmi.MenuItemID = mi.MenuItemID
LEFT JOIN dbo.[Role] r
    ON r.RoleID = rmi.RoleID
WHERE mi.MenuItemCategoryID IN (10,20,30,90)
  AND mi.MenuItemRowID = 0
  AND mi.MenuItemTypeID = 0
ORDER BY mi.MenuItemCategoryID, mi.MenuItemID, rmi.RoleID;
