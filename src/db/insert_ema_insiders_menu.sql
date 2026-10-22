-- Adds the "EMA Insiders" screen to the DB-driven navigation as a TOP-LEVEL item in the
-- Support category (90). Mirrors insert_ema_statistics_menu.sql. URL matches
-- EmaInsiders.razor (/ema-insiders).
--
-- MenuItemID 6900 = a Row-1 (header) item that sorts BEFORE EMA Statistics (6950) and
-- Tables (7000), so it appears first in the Support nav bar. Granted to RoleID 10000
-- (eTran) — the role that gates the Support category.

-- 1. Insert MenuItem 6900 for EMA Insiders if it does not exist
IF NOT EXISTS (SELECT 1 FROM MenuItem WHERE MenuItemID = 6900)
BEGIN
    INSERT INTO MenuItem (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID, MenuName, ScreenTitle, URL, IsActive)
    VALUES (90, 1, 6900, 1, 'EMA Insiders', 'EMA Insiders', '/ema-insiders', 'Y');
END;

-- 2. Associate MenuItem 6900 with Role 10000 (eTran) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 10000 AND MenuItemID = 6900)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (10000, 6900);
END;
GO
