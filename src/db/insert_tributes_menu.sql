-- 1. Insert MenuItem 4050 for Tributes Report if it does not exist
IF NOT EXISTS (SELECT 1 FROM MenuItem WHERE MenuItemID = 4050)
BEGIN
    INSERT INTO MenuItem (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID, MenuName, ScreenTitle, URL, IsActive)
    VALUES (20, 2, 4050, 11, 'Tributes', 'Tribute Report', '/tributes', 'Y');
END;

-- 2. Associate MenuItem 4050 with Role 10 (Administrator) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 10 AND MenuItemID = 4050)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (10, 4050);
END;

-- 3. Associate MenuItem 4050 with Role 20 (Data Entry) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 20 AND MenuItemID = 4050)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (20, 4050);
END;
GO
