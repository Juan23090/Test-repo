/*
    Task 31 - Public Web Page menu text update

    IMPORTANT:
    This is a database data change and must be reviewed/approved by Roger
    before it is executed in any environment.

    The application patch does not execute this script automatically.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CanadianServersCount int =
(
    SELECT COUNT(*)
    FROM dbo.MenuItem
    WHERE URL = '/canadian-servers'
);

IF @CanadianServersCount <> 1
BEGIN
    THROW 50001,
        'Expected exactly one MenuItem for /canadian-servers. No update was applied.',
        1;
END;

UPDATE dbo.MenuItem
SET
    MenuName = 'Canadian Data Sovereignty & Security',
    ScreenTitle = 'Canadian Data Sovereignty & Security'
WHERE URL = '/canadian-servers';

/*
    Task 31.1 supersedes the earlier public-menu plan for PADS from Bank
    Statements. Do not create public MenuItemID 10270. The three graphic
    workflow screens are handled as Organization / Setup menu items by the
    separate Task 31.1 review script.
*/
