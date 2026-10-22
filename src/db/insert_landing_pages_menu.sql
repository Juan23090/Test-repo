-- Registers the KPI landing pages in the DB-driven navigation (KPI Tweaks 20260814, items 7-9).
--
-- Background: a category's landing page is resolved by GetCategoryLandingPage.cs /
-- GetDonorCategoryLandingPage.cs, which prefer a MenuItem whose DisplayControlID = 'L' and
-- otherwise fall back to the first DisplayControlID IN ('M','B') + MenuItemTypeID = 11 item
-- with a URL, ordered by MenuItemRowID then MenuItemID.
--
-- No row anywhere currently uses 'L', so every category falls through to that fallback and
-- lands on an arbitrary screen:
--     Organizations (20) -> /mailing-lists-index
--     Donors        (30) -> /my-tax-receipts
--     EMA Insiders  (90) -> /banks
--
-- 'L' rows are deliberately SEPARATE from the visible menu rows: the menu query
-- (GetMenuItemsByUser.cs) filters DisplayControlID IN ('M','B'), so flipping an existing
-- menu row to 'L' would delete it from the nav. Each landing row below is therefore a new,
-- menu-invisible entry that only the landing-page resolver sees.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Donors (category 30) -> /donor-dashboard        [spec item 7]
-- ─────────────────────────────────────────────────────────────────────────────

-- 1a. The existing "Donor KPIs" menu row points at /donor-kpis, which is not a route in the
--     app (the page is DonorDashboard.razor @page "/donor-dashboard"), so the menu entry is a
--     dead link today. Repoint it.
UPDATE dbo.MenuItem
SET    URL = '/donor-dashboard'
WHERE  MenuItemID = 30120
  AND  URL = '/donor-kpis';

-- 1b. Landing row for the donor dashboard.
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 30110)
BEGIN
    INSERT INTO dbo.MenuItem
        (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID,
         MenuName, ScreenTitle, DisplayControlID, URL, IsActive)
    VALUES
        (30, 2, 30110, 11, 'Donor KPIs', 'Donor KPIs', 'L', '/donor-dashboard', 'Y');
END;

-- Donor roles: 3000 (Donor - No Credit Cards), 3010 (Donor - Credit Cards).
IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 3000 AND MenuItemID = 30110)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (3000, 30110);

IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 3010 AND MenuItemID = 30110)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (3010, 30110);

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. EMA Insiders (category 90) -> /ema-insiders     [spec item 8]
-- ─────────────────────────────────────────────────────────────────────────────

-- 2a. Landing row for the EMA Insiders graph page. EmaInsiders.razor (@page "/ema-insiders")
--     has never been registered in MenuItem at all.
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 95050)
BEGIN
    INSERT INTO dbo.MenuItem
        (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID,
         MenuName, ScreenTitle, DisplayControlID, URL, IsActive)
    VALUES
        (90, 2, 95050, 11, 'Insider KPIs', 'EMA Insider KPIs', 'L', '/ema-insiders', 'Y');
END;

-- Role 10 = Organization Administrator (how this is tested), 10000 = eTran (gates category 90).
IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 10 AND MenuItemID = 95050)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (10, 95050);

IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 10000 AND MenuItemID = 95050)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (10000, 95050);

-- 2b. Visible menu row for the same page. The 'L' row above is landing-only (the nav query
--     filters DisplayControlID IN ('M','B')), so without this there is no way back to the
--     Insiders page once you navigate to Banks/Tables/etc. Mirrors how /kpis and
--     /donor-dashboard each have both a menu row and a landing row.
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 95060)
BEGIN
    INSERT INTO dbo.MenuItem
        (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID,
         MenuName, ScreenTitle, DisplayControlID, URL, IsActive)
    VALUES
        (90, 2, 95060, 11, 'Insider KPIs', 'EMA Insider KPIs', 'M', '/ema-insiders', 'Y');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 10 AND MenuItemID = 95060)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (10, 95060);

IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 10000 AND MenuItemID = 95060)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (10000, 95060);

-- 2c. The category-90 header row carries a placeholder URL (/ema-insiders-temp-header) that
--     resolves to nothing. Category headers elsewhere (19000 Organizations, 30100 Donors)
--     have an empty URL, so match that convention now that a real landing row exists.
UPDATE dbo.MenuItem
SET    URL = '',
       ScreenTitle = 'EMA Insiders'
WHERE  MenuItemID = 95000
  AND  URL = '/ema-insiders-temp-header';

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Organizations (category 20) -> /kpis
-- ─────────────────────────────────────────────────────────────────────────────
-- Not in the spec, but included so all three landing pages are deterministic: without an 'L'
-- row the org landing resolves to /mailing-lists-index, whereas the KPIs screen is the org
-- landing page in Roger's task screenshot. Drop this section if that is not wanted.
-- MenuItem 24950 (KPIs, DisplayControlID 'M') stays untouched so KPIs remains in the nav.

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 19010)
BEGIN
    INSERT INTO dbo.MenuItem
        (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID,
         MenuName, ScreenTitle, DisplayControlID, URL, IsActive)
    VALUES
        (20, 2, 19010, 11, 'KPIs', 'Key Performance Indicators', 'L', '/kpis', 'Y');
END;

-- Role 10 = Organization Administrator, 20 = Data Entry (mirrors MenuItem 24950's grants).
IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 10 AND MenuItemID = 19010)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (10, 19010);

IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 20 AND MenuItemID = 19010)
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (20, 19010);
GO
