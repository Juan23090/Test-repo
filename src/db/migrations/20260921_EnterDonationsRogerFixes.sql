SET XACT_ABORT ON;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = 500
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
        500,
        'Manually Keyed Donation Created',
        'N',
        'N',
        'N',
        'An authenticated organization user manually created the donation.'
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = 520
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
        520,
        'Manually Edited Donation',
        'N',
        'N',
        'N',
        'An authenticated organization user manually edited the donation.'
    );
END;
GO
