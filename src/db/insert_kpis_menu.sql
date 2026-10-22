-- Adds the "KPIs" screen to the DB-driven sub-navigation.
-- Mirrors insert_gifts_menu.sql. URL matches the KPIs.razor route (/kpis).
-- Sits beside Dashboard (4900) under the Reports header in the Operations category.

-- 1. Insert MenuItem 4950 for KPIs if it does not exist
IF NOT EXISTS (SELECT 1 FROM MenuItem WHERE MenuItemID = 4950)
BEGIN
    INSERT INTO MenuItem (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID, MenuName, ScreenTitle, URL, IsActive)
    VALUES (20, 2, 4950, 11, 'KPIs', 'KPIs', '/kpis', 'Y');
END;

-- 2. Associate MenuItem 4950 with Role 10 (Administrator) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 10 AND MenuItemID = 4950)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (10, 4950);
END;

-- 3. Associate MenuItem 4950 with Role 20 (Data Entry) if it does not exist
IF NOT EXISTS (SELECT 1 FROM RoleMenuItem WHERE RoleID = 20 AND MenuItemID = 4950)
BEGIN
    INSERT INTO RoleMenuItem (RoleID, MenuItemID)
    VALUES (20, 4950);
END;
GO
