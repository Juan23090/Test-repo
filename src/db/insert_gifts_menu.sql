-- Adds the "Gifts" report tab to the DB-driven sub-navigation.
-- Mirrors insert_tributes_menu.sql. URL matches the Gifts.razor route (/gift-report).

-- 1. Insert MenuItem 4060 for Gift Report if it does not exist
IF NOT EXISTS (SELECT 1 FROM MenuItem WHERE MenuItemID = 4060)
BEGIN
    INSERT INTO MenuItem (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID, MenuName, ScreenTitle, URL, IsActive)
    VALUES (20, 2, 4060, 11, 'Gifts', 'Gift Report', '/gift-report', 'Y');
END;

-- 2. Associate MenuItem 4060 with Role 10 (Administrator) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 10 AND MenuItemID = 4060)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (10, 4060);
END;

-- 3. Associate MenuItem 4060 with Role 20 (Data Entry) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 20 AND MenuItemID = 4060)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (20, 4060);
END;
GO
