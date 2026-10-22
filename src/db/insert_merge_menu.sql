-- 1. Insert MenuItem 3600 for Merge if it does not exist
IF NOT EXISTS (SELECT 1 FROM MenuItem WHERE MenuItemID = 3600)
BEGIN
    INSERT INTO MenuItem (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID, MenuName, ScreenTitle, URL, IsActive)
    VALUES (20, 2, 3600, 11, 'Merge', 'Merge Data', '/merge-data', 'Y');
END;

-- 2. Associate MenuItem 3600 with Role 10 (Administrator) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 10 AND MenuItemID = 3600)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (10, 3600);
END;
GO
