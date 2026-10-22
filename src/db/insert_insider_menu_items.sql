-- Makes the EMA Statistics and Insider KPIs pages reachable from the Insiders nav (category 90).
--
-- Background: row-1 MenuItems are dropdown parents and own row-2 children by ID range
-- (DataDrivenNavigation.razor). When a parent has children, HandleParentMenuClickAsync only
-- toggles the dropdown -- it never navigates to the parent's own URL. So a URL parked on a
-- row-1 header is dead.
--
-- That is why /ema-statistics is unreachable today: it sits on MenuItem 91950, a row-1 header
-- named "Setup" that owns Tables/Settings/Banks/ChMS, so its URL is never used and no row-2
-- child points at the page.
--
-- Separately, the row-2 menu entry for /ema-insiders was lost when the menu was renumbered;
-- only the landing-page row (90050, DisplayControlID 'L') survived, and 'L' rows are
-- deliberately hidden from the nav. That also left the "EMA Insiders" header (90000) with no
-- menu-visible children, so it renders as a disabled label.
--
-- Both new rows use IDs between 90000 and 91950 so the "EMA Insiders" header owns them.
-- Role 9000 = "Insider with Everything" (matches the existing category-90 grants).

-- 1. Insider KPIs -> /ema-insiders (restores the menu entry lost in the renumbering)
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 90060)
BEGIN
    INSERT INTO dbo.MenuItem
        (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID,
         MenuName, ScreenTitle, DisplayControlID, URL, IsActive)
    VALUES
        (90, 2, 90060, 11, 'Insider KPIs', 'EMA Insider KPIs', 'M', '/ema-insiders', 'Y');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 9000 AND MenuItemID = 90060)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (9000, 90060);

-- 2. EMA Statistics -> /ema-statistics (the page exists but has never had a reachable entry)
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 90070)
BEGIN
    INSERT INTO dbo.MenuItem
        (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID,
         MenuName, ScreenTitle, DisplayControlID, URL, IsActive)
    VALUES
        (90, 2, 90070, 11, 'EMA Statistics', 'EMA Statistics', 'M', '/ema-statistics', 'Y');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 9000 AND MenuItemID = 90070)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (9000, 90070);
GO
