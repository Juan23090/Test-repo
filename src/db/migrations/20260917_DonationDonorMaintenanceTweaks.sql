SET XACT_ABORT ON;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = 115
)
BEGIN
    INSERT INTO dbo.DonationStep
    (
        DonationStepID,
        Description,
        NotifyDonor,
        NotifyOrgAdmin,
        NotifyEMASupport,
        Explanation
    )
    VALUES
    (
        115,
        'Non-Profit Office Notification Loaded',
        'N',
        'N',
        'N',
        'The non-profit office opened a donation containing donor-directed changes.'
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = 120
)
BEGIN
    INSERT INTO dbo.DonationStep
    (
        DonationStepID,
        Description,
        NotifyDonor,
        NotifyOrgAdmin,
        NotifyEMASupport,
        Explanation
    )
    VALUES
    (
        120,
        'Non-Profit Office Notification Saved',
        'N',
        'N',
        'N',
        'The non-profit office saved a donation containing donor-directed changes.'
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = 1000
)
BEGIN
    INSERT INTO dbo.DonationStep
    (
        DonationStepID,
        Description,
        NotifyDonor,
        NotifyOrgAdmin,
        NotifyEMASupport,
        Explanation
    )
    VALUES
    (
        1000,
        'Donation Changed Online',
        'N',
        'N',
        'N',
        'An authenticated organization user changed the donation online.'
    );
END;
GO
