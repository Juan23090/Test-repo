-- Adds the "EMA Statistics" screen to the DB-driven navigation as a TOP-LEVEL item in the
-- Support category (90). Mirrors insert_kpis_menu.sql. URL matches EmaStatistics.razor (/ema-statistics).
--
-- MenuItemID 6950 = a Row-1 (header) item that sorts BEFORE Tables (7000), so it appears
-- first in the Support nav bar. It must stay below 7000: a Row-1 id placed between an existing
-- header and its Row-2 children would steal that header's dropdown items.
-- Granted to RoleID 10000 (eTran) — the role that gates the Support category.

-- 1. Insert MenuItem 6950 for EMA Statistics if it does not exist
IF NOT EXISTS (SELECT 1 FROM MenuItem WHERE MenuItemID = 6950)
BEGIN
    INSERT INTO MenuItem (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID, MenuName, ScreenTitle, URL, IsActive)
    VALUES (90, 1, 6950, 1, 'EMA Statistics', 'EMA Statistics', '/ema-statistics', 'Y');
END;

-- 2. Associate MenuItem 6950 with Role 10000 (eTran) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 10000 AND MenuItemID = 6950)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (10000, 6950);
END;
GO
