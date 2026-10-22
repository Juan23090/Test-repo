-- Saves a user's fund shortlist in one call: the overlay commits on Save, so the whole edited
-- list arrives together rather than one row per change.
--
-- @JsonPayload shape: { "Items": [ { "EMAFundID": 1, "Selected": "Y", "Sequence": 1 }, ... ] }
--
-- Only rows that already belong to this Org & User are updated -- a fund id the user has no
-- shortlist row for is ignored rather than inserted, so a stale client cannot resurrect a fund
-- that sp_SyncUserFundShortList has removed.

CREATE OR ALTER PROCEDURE dbo.sp_SaveUserFundShortList
    @EMAOrganizationID INT,
    @UserID            INT,
    @JsonPayload       NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF ISJSON(@JsonPayload) = 0
    BEGIN
        THROW 52090, 'The shortlist payload is not valid JSON.', 1;
    END;

    DECLARE @Items TABLE
    (
        EMAFundID INT     NOT NULL PRIMARY KEY,
        Selected  CHAR(1) NULL,
        Sequence  INT     NULL
    );

    INSERT INTO @Items
    (
        EMAFundID,
        Selected,
        Sequence
    )
    SELECT
        TRY_CONVERT(
            INT,
            JSON_VALUE(item.[value], '$.EMAFundID')
        ),
        CASE
            WHEN JSON_VALUE(item.[value], '$.Selected') = 'Y' THEN 'Y'
            ELSE 'N'
        END,
        TRY_CONVERT(
            INT,
            JSON_VALUE(item.[value], '$.Sequence')
        )
    FROM OPENJSON(
             @JsonPayload,
             '$.Items'
         ) AS item
    WHERE TRY_CONVERT(
              INT,
              JSON_VALUE(item.[value], '$.EMAFundID')
          ) IS NOT NULL;

    UPDATE s
    SET
        s.Selected = i.Selected,
        s.Sequence = i.Sequence
    FROM dbo.OrganizationUserFundShortList AS s
    INNER JOIN @Items AS i
        ON i.EMAFundID = s.EMAFundID
    WHERE s.UserID = @UserID
      AND s.EMAOrganizationID = @EMAOrganizationID;
END;
GO
