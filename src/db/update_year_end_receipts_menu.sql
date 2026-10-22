-- Spell out "Year End" on the organisation-side receipts screen (Roger's request:
-- no need to shorten "YE"). MenuItem 4975 drives the menu label and the screen title.
-- Note: the MenuItem.URL is derived from the ScreenTitle slug, so renaming the screen
-- to "Year End Receipts" moves the route to /year-end-receipts; the page's @page route
-- (Receipts.razor) is updated to match.

UPDATE dbo.MenuItem
SET MenuName = 'Year End Receipts',
    ScreenTitle = 'Year End Receipts'
WHERE MenuItemID = 4975;
GO
