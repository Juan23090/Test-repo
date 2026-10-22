-- InstallMailingListsSchema.sql
-- Create Lookup and Core Tables for Mailing Lists Management

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MailingListOutputFormat')
BEGIN
    EXEC('
    CREATE TABLE dbo.MailingListOutputFormat (
        OutputFormatID INT IDENTITY(1,1) PRIMARY KEY,
        Description VARCHAR(50) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1
    );

    INSERT INTO dbo.MailingListOutputFormat (Description, IsActive) VALUES
    (''CSV'', 1),
    (''Spreadsheet'', 1),
    (''PDF'', 1);
    ');
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MailingListSendMethod')
BEGIN
    EXEC('
    CREATE TABLE dbo.MailingListSendMethod (
        SendMethodID INT IDENTITY(1,1) PRIMARY KEY,
        Description VARCHAR(50) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1
    );

    INSERT INTO dbo.MailingListSendMethod (Description, IsActive) VALUES
    (''Email'', 1),
    (''Postal Mail'', 1);
    ');
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MailingListAndOr')
BEGIN
    EXEC('
    CREATE TABLE dbo.MailingListAndOr (
        AndOrID INT IDENTITY(1,1) PRIMARY KEY,
        Description VARCHAR(10) NOT NULL
    );

    INSERT INTO dbo.MailingListAndOr (Description) VALUES
    (''And''),
    (''Or''),
    (''Neither'');
    ');
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MailingListReason')
BEGIN
    EXEC('
    CREATE TABLE dbo.MailingListReason (
        MailingListReasonID INT IDENTITY(1,1) PRIMARY KEY,
        Description VARCHAR(255) NOT NULL
    );
    ');
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrganizationMailingList')
BEGIN
    EXEC('
    CREATE TABLE dbo.OrganizationMailingList (
        MailingListID INT IDENTITY(1,1) PRIMARY KEY,
        EMAOrganizationID INT NOT NULL,
        Name VARCHAR(255) NOT NULL,
        Description VARCHAR(MAX) NULL,
        Donation1StartDate DATE NULL,
        Donation1EndDate DATE NULL,
        Donation1MoneyFrom DECIMAL(18,2) NULL,
        Donation1MoneyTo DECIMAL(18,2) NULL,
        AndOr VARCHAR(10) NULL DEFAULT ''And'',
        Donation2StartDate DATE NULL,
        Donation2EndDate DATE NULL,
        Donation2MoneyFrom DECIMAL(18,2) NULL,
        Donation2MoneyTo DECIMAL(18,2) NULL,
        NumberDonorsExtracted INT NULL DEFAULT 0,
        NumberDonorsOptedIn INT NULL DEFAULT 0,
        NumberDonorsOptedOut INT NULL DEFAULT 0,
        NumberDonorsInMailingList INT NULL DEFAULT 0,
        OutputFormatID INT NULL FOREIGN KEY REFERENCES dbo.MailingListOutputFormat(OutputFormatID),
        SendMethodID INT NULL FOREIGN KEY REFERENCES dbo.MailingListSendMethod(SendMethodID),
        IsActive BIT NOT NULL DEFAULT 1,
        DateTimeCreated DATETIME2 NOT NULL DEFAULT GETDATE(),
        CreatedByUserID INT NULL,
        DateTimeUpdated DATETIME2 NULL,
        UpdatedByUserID INT NULL,
        CONSTRAINT FK_OrgMailingList_Org FOREIGN KEY (EMAOrganizationID) REFERENCES dbo.Organization(EMAOrganizationID)
    );
    ');
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrganizationMailingListDonor')
BEGIN
    EXEC('
    CREATE TABLE dbo.OrganizationMailingListDonor (
        EMAOrganizationID INT NOT NULL,
        MailingListID INT NOT NULL,
        EMADonorID INT NOT NULL,
        MailingListReasonID INT NULL FOREIGN KEY REFERENCES dbo.MailingListReason(MailingListReasonID),
        DateTimeCreated DATETIME2 NOT NULL DEFAULT GETDATE(),
        CreatedByUserID INT NULL,
        OptedIn BIT NOT NULL DEFAULT 0,
        OptedOut BIT NOT NULL DEFAULT 0,
        PRIMARY KEY (EMAOrganizationID, MailingListID, EMADonorID),
        CONSTRAINT FK_OrgMailingListDonor_List FOREIGN KEY (MailingListID) REFERENCES dbo.OrganizationMailingList(MailingListID),
        CONSTRAINT FK_OrgMailingListDonor_Donor FOREIGN KEY (EMAOrganizationID, EMADonorID) REFERENCES dbo.Donor(EMAOrganizationID, EMADonorID)
    );
    ');
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrganizationMailingListInstance')
BEGIN
    EXEC('
    CREATE TABLE dbo.OrganizationMailingListInstance (
        OrganizationID INT NOT NULL,
        MailingListID INT NOT NULL,
        MailingListInstanceID INT IDENTITY(1,1) NOT NULL,
        DateTimeCreated DATETIME2 NOT NULL DEFAULT GETDATE(),
        DonorsSelected INT NOT NULL DEFAULT 0,
        DonorsOptedIn INT NOT NULL DEFAULT 0,
        DonorsOptedOut INT NOT NULL DEFAULT 0,
        PRIMARY KEY (MailingListInstanceID),
        CONSTRAINT FK_OrgMailingListInstance_List FOREIGN KEY (MailingListID) REFERENCES dbo.OrganizationMailingList(MailingListID)
    );
    ');
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrganizationMailingListInstanceDonor')
BEGIN
    EXEC('
    CREATE TABLE dbo.OrganizationMailingListInstanceDonor (
        EMAOrganizationID INT NOT NULL,
        MailingListID INT NOT NULL,
        MailingListInstanceID INT NOT NULL,
        EMADonorID INT NOT NULL,
        MessageID VARCHAR(255) NULL,
        EmailAddress VARCHAR(255) NULL,
        OfficialDonorName VARCHAR(255) NULL,
        Address VARCHAR(255) NULL,
        City VARCHAR(100) NULL,
        ProvinceStateCode CHAR(2) NULL,
        PostalCode VARCHAR(20) NULL,
        Country VARCHAR(100) NULL,
        DateTimeCreated DATETIME2 NOT NULL DEFAULT GETDATE(),
        DateTimeSent DATETIME2 NULL,
        DateTimeDelivered DATETIME2 NULL,
        DateTimeOpened DATETIME2 NULL,
        DateTimeClicked DATETIME2 NULL,
        PRIMARY KEY (MailingListInstanceID, EMADonorID),
        CONSTRAINT FK_InstanceDonor_Instance FOREIGN KEY (MailingListInstanceID) REFERENCES dbo.OrganizationMailingListInstance(MailingListInstanceID),
        CONSTRAINT FK_InstanceDonor_Donor FOREIGN KEY (EMAOrganizationID, EMADonorID) REFERENCES dbo.Donor(EMAOrganizationID, EMADonorID)
    );
    ');
END;
