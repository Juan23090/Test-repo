-- Adds the "Receipts" screen to the DB-driven donor sub-navigation.
-- Mirrors insert_kpis_menu.sql. URL matches the DonorReceipts.razor route (/my-tax-receipts).
-- Sits beside the other donor items (30000-30050) in the Donors category (30),
-- linked to both donor roles (3000, 3010) like the existing donor menu items.

-- 1. Insert MenuItem 30060 for donor tax receipts if it does not exist
IF NOT EXISTS (SELECT 1 FROM MenuItem WHERE MenuItemID = 30060)
BEGIN
    INSERT INTO MenuItem (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID, MenuName, ScreenTitle, URL, IsActive)
    VALUES (30, 1, 30060, 11, 'Receipts', 'My Tax Receipts', '/my-tax-receipts', 'Y');
END;

-- 2. Associate MenuItem 30060 with donor Role 3000 if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 3000 AND MenuItemID = 30060)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (3000, 30060);
END;

-- 3. Associate MenuItem 30060 with donor Role 3010 if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 3010 AND MenuItemID = 30060)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (3010, 30060);
END;
GO
