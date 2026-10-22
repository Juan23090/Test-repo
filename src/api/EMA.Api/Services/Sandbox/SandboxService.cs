using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using MimeKit;
using MimeKit.Utils;
using EMA.Api.DTOs;
using EMA.Api.Services.Email;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Sandbox;

public sealed class SandboxService :
    ISandboxService
{
    private const int BrochureCategoryID = 10;
    private const int OperationsCategoryID = 20;

    private readonly string _connectionString;
    private readonly int _templateOrganizationID;
    private readonly int? _templateUserID;
    private readonly int _createdByUserID;
    private readonly int _maxTemplateDonations;
    private readonly int _activeDays;
    private readonly string _sandboxEmailDomain;
    private readonly string? _acsEmailSender;

    private readonly IDonationEmailIngestionService _donationEmailIngestionService;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly ISandboxEmailAliasProvisioner _emailAliasProvisioner;
    private readonly ILogger<SandboxService> _logger;

    public SandboxService(
        IConfiguration configuration,
        IDonationEmailIngestionService donationEmailIngestionService,
        IEmailNotificationService emailNotificationService,
        ISandboxEmailAliasProvisioner emailAliasProvisioner,
        ILogger<SandboxService> logger)
    {
        _donationEmailIngestionService = donationEmailIngestionService;
        _emailNotificationService = emailNotificationService;
        _emailAliasProvisioner = emailAliasProvisioner;
        _logger = logger;

        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        _templateOrganizationID =
            ParseRequiredPositiveInt(
                configuration["SandboxTemplateOrganizationID"]
                ?? Environment.GetEnvironmentVariable(
                    "SandboxTemplateOrganizationID"),
                "SandboxTemplateOrganizationID");

        _templateUserID =
            ParseOptionalPositiveInt(
                configuration["SandboxTemplateUserID"]
                ?? Environment.GetEnvironmentVariable(
                    "SandboxTemplateUserID"));

        _createdByUserID =
            ParseRequiredPositiveInt(
                configuration["AUTOMATED_SERVICE_USER_ID"]
                ?? Environment.GetEnvironmentVariable(
                    "AUTOMATED_SERVICE_USER_ID"),
                "AUTOMATED_SERVICE_USER_ID");

        _maxTemplateDonations =
            ParseOptionalPositiveInt(
                configuration["SandboxMaxTemplateDonations"]
                ?? Environment.GetEnvironmentVariable(
                    "SandboxMaxTemplateDonations"))
            ?? 100;

        _activeDays =
            ParseOptionalPositiveInt(
                configuration["SandboxActiveDays"]
                ?? Environment.GetEnvironmentVariable(
                    "SandboxActiveDays"))
            ?? 180;

        _sandboxEmailDomain =
            (
                configuration["SandboxEmailDomain"]
                ?? Environment.GetEnvironmentVariable(
                    "SandboxEmailDomain")
                ?? "emailmoney.app"
            )
            .Trim()
            .TrimStart('@');

        _acsEmailSender =
            configuration["ACS_EMAIL_SENDER"]
            ?? Environment.GetEnvironmentVariable(
                "ACS_EMAIL_SENDER");

    }

    public async Task<SandboxWorkspaceSummaryDto?> GetCurrentSandboxAsync(
        string clientIPAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedIPAddress =
            NormalizeIPAddress(
                clientIPAddress);

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        return await GetCurrentSandboxAsync(
            connection,
            normalizedIPAddress,
            cancellationToken);
    }

    public async Task<SandboxWorkspaceSummaryDto?> GetSandboxAsync(
        int sandboxOrganizationID,
        CancellationToken cancellationToken = default)
    {
        if (sandboxOrganizationID <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT TOP (1)
                o.EMAOrganizationID AS SandboxOrganizationID,
                u.UserID AS SandboxUserID,
                o.OrganizationName,
                u.PhoneNumber,
                o.SandboxEtransferEmail AS SandboxEmailAddress,
                o.DateTimeCreated,
                activity.LastDonationDate
            FROM dbo.[User] u
            INNER JOIN dbo.Organization o
                ON o.EMAOrganizationID =
                   u.SandboxOrganizationID
            OUTER APPLY
            (
                SELECT
                    MAX(d.DateTimeReceived) AS LastDonationDate
                FROM dbo.Donation d
                WHERE d.EMAOrganizationID =
                      o.EMAOrganizationID
            ) activity
            WHERE u.IsActive = 1
              AND u.SandboxOrganizationID =
                  @SandboxOrganizationID
              AND ISNULL(o.OrganizationStatusID, -1) = 0
              AND COALESCE(
                    activity.LastDonationDate,
                    o.DateTimeCreated,
                    u.DateTimeCreated,
                    SYSUTCDATETIME()
                  ) >= DATEADD(
                        DAY,
                        -@ActiveDays,
                        SYSUTCDATETIME())
            ORDER BY u.UserID;
            """;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@SandboxOrganizationID",
            SqlDbType.Int)
            .Value =
                sandboxOrganizationID;

        command.Parameters.Add(
            "@ActiveDays",
            SqlDbType.Int)
            .Value =
                _activeDays;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        return await reader.ReadAsync(
                cancellationToken)
            ? ReadWorkspace(
                reader)
            : null;
    }

    public async Task<SandboxWorkspaceSummaryDto> CreateSandboxAsync(
        string clientIPAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedIPAddress =
            NormalizeIPAddress(
                clientIPAddress);

        var existingWorkspace =
            await GetCurrentSandboxAsync(
                normalizedIPAddress,
                cancellationToken);

        if (existingWorkspace is not null)
        {
            throw new SandboxAlreadyExistsException();
        }

        for (var attempt = 1; attempt <= 25; attempt++)
        {
            var phoneNumber =
                GenerateSandboxPhoneNumber();

            var sandboxEmailAddress =
                $"sandbox-{phoneNumber}@{_sandboxEmailDomain}"
                    .ToLowerInvariant();

            try
            {
                var workspace =
                    await ExecuteCreateSandboxProcedureAsync(
                        normalizedIPAddress,
                        phoneNumber,
                        sandboxEmailAddress,
                        cancellationToken);

                await _emailAliasProvisioner
                    .EnsureAliasAsync(
                        workspace.SandboxOrganizationID,
                        workspace.SandboxEmailAddress,
                        cancellationToken);

                return workspace;
            }
            catch (SqlException exception)
                when (IsPhoneNumberCollision(
                    exception))
            {
                _logger.LogWarning(
                    exception,
                    "Generated sandbox phone number collided on attempt {Attempt}. Retrying.",
                    attempt);
            }
        }

        throw new InvalidOperationException(
            "Unable to generate a unique sandbox phone number after 25 attempts.");
    }

    public async Task<LoginResponseDto?> ResumeSandboxAsync(
        int sandboxOrganizationID,
        string clientIPAddress,
        CancellationToken cancellationToken = default)
    {
        if (sandboxOrganizationID <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sandboxOrganizationID));
        }

        _ =
            NormalizeIPAddress(
                clientIPAddress);

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        var sandboxUserID =
            await GetOwnedSandboxUserIDAsync(
                connection,
                sandboxOrganizationID,
                cancellationToken);

        if (!sandboxUserID.HasValue)
        {
            throw new UnauthorizedAccessException(
                "That sandbox is not active.");
        }

        return await BuildLoginResponseAsync(
            connection,
            sandboxOrganizationID,
            sandboxUserID.Value,
            cancellationToken);
    }

    public async Task<LoginResponseDto?> ResumeSandboxByPhoneAsync(
        string phoneNumber,
        string clientIPAddress,
        CancellationToken cancellationToken = default)
    {
        var phoneDigits =
            DigitsOnly(
                phoneNumber);

        if (phoneDigits.Length != 10)
        {
            throw new ArgumentException(
                "Please enter a valid 10 digit phone number.",
                nameof(phoneNumber));
        }

        var normalizedIPAddress =
            NormalizeIPAddress(
                clientIPAddress);

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            SELECT TOP (1)
                u.UserID,
                u.SandboxOrganizationID
            FROM dbo.[User] u
            INNER JOIN dbo.Organization o
                ON o.EMAOrganizationID =
                   u.SandboxOrganizationID
            WHERE u.IsActive = 1
              AND u.SandboxOrganizationID IS NOT NULL
              AND ISNULL(o.OrganizationStatusID, -1) = 0
              AND COALESCE(
                    (
                        SELECT MAX(d.DateTimeReceived)
                        FROM dbo.Donation d
                        WHERE d.EMAOrganizationID =
                              o.EMAOrganizationID
                    ),
                    o.DateTimeCreated,
                    u.DateTimeCreated,
                    SYSUTCDATETIME()
                  ) >= DATEADD(
                        DAY,
                        -@ActiveDays,
                        SYSUTCDATETIME())
              AND RIGHT(
                  REPLACE(
                      REPLACE(
                          REPLACE(
                              REPLACE(
                                  REPLACE(
                                      REPLACE(
                                          ISNULL(
                                              u.PhoneNumber,
                                              ''
                                          ),
                                          '(',
                                          ''
                                      ),
                                      ')',
                                      ''
                                  ),
                                  '-',
                                  ''
                              ),
                              ' ',
                              ''
                          ),
                          '.',
                          ''
                      ),
                      '+',
                      ''
                  ),
                  10
              ) = @PhoneDigits
            ORDER BY
                u.UserID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@PhoneDigits",
            SqlDbType.NVarChar,
            10)
            .Value = phoneDigits;

        command.Parameters.Add(
            "@ActiveDays",
            SqlDbType.Int)
            .Value = _activeDays;

        int? userID = null;
        int? organizationID = null;

        await using (var reader =
            await command.ExecuteReaderAsync(
                cancellationToken))
        {
            if (await reader.ReadAsync(
                    cancellationToken))
            {
                userID =
                    Convert.ToInt32(
                        reader["UserID"]);

                organizationID =
                    Convert.ToInt32(
                        reader["SandboxOrganizationID"]);
            }
        }

        if (!userID.HasValue ||
            !organizationID.HasValue)
        {
            return null;
        }

        const string updateSql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE dbo.[User]
            SET
                SandboxIPAddress = NULL,
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedByUserID = @UpdatedByUserID
            WHERE SandboxIPAddress = @SandboxIPAddress
              AND SandboxOrganizationID IS NOT NULL
              AND UserID <> @UserID;

            UPDATE dbo.[User]
            SET
                SandboxIPAddress = @SandboxIPAddress,
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedByUserID = @UpdatedByUserID
            WHERE UserID = @UserID
              AND SandboxOrganizationID = @OrganizationID;

            IF @@ROWCOUNT <> 1
                THROW 51050, 'The sandbox user could not be updated.', 1;

            COMMIT TRANSACTION;
            """;

        await using (var updateCommand =
            new SqlCommand(
                updateSql,
                connection))
        {
            updateCommand.Parameters.Add(
                "@SandboxIPAddress",
                SqlDbType.VarChar,
                50)
                .Value = normalizedIPAddress;

            updateCommand.Parameters.Add(
                "@UpdatedByUserID",
                SqlDbType.Int)
                .Value = _createdByUserID;

            updateCommand.Parameters.Add(
                "@UserID",
                SqlDbType.Int)
                .Value = userID.Value;

            updateCommand.Parameters.Add(
                "@OrganizationID",
                SqlDbType.Int)
                .Value = organizationID.Value;

            await updateCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        return await BuildLoginResponseAsync(
            connection,
            organizationID.Value,
            userID.Value,
            cancellationToken);
    }

    public async Task<SandboxTestEmailDraftDto?> GetTestEmailDraftAsync(
        int sandboxOrganizationID,
        string clientIPAddress,
        CancellationToken cancellationToken = default)
    {
        var workspace =
            await GetSandboxAsync(
                sandboxOrganizationID,
                cancellationToken);

        if (workspace is null)
        {
            throw new UnauthorizedAccessException(
                "That sandbox is not active.");
        }

        return new SandboxTestEmailDraftDto
        {
            SandboxOrganizationID =
                workspace.SandboxOrganizationID,

            SandboxEmailAddress =
                workspace.SandboxEmailAddress,

            SenderName =
                "Some Guy",

            SenderEmailAddress =
                "test.donor@example.com",

            Amount =
                100.00m,

            ReferenceNumber =
                $"SBX{RandomNumberGenerator.GetInt32(
                    10000000,
                    99999999)}",

            Message =
                "Testing Email Money App"
        };
    }

    public async Task<SendSandboxTestEmailResponseDto> SendTestEmailAsync(
        SendSandboxTestEmailRequestDto request,
        string clientIPAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        ValidateTestEmailRequest(
            request);

        var workspace =
            await GetSandboxAsync(
                request.SandboxOrganizationID,
                cancellationToken);

        if (workspace is null)
        {
            throw new UnauthorizedAccessException(
                "That sandbox is not active.");
        }

        if (string.IsNullOrWhiteSpace(
                workspace.SandboxEmailAddress) ||
            !IsValidEmailAddress(
                workspace.SandboxEmailAddress))
        {
            throw new InvalidOperationException(
                "The sandbox does not have a valid eTransfer email address.");
        }

        if (string.IsNullOrWhiteSpace(
                _acsEmailSender) ||
            !IsValidEmailAddress(
                _acsEmailSender))
        {
            throw new InvalidOperationException(
                "Missing or invalid ACS_EMAIL_SENDER setting.");
        }

        var subject =
            $"INTERAC e-Transfer automatically deposited: " +
            $"{request.SenderName.Trim()} sent you money. [EMA-SANDBOX]";

        var htmlBody =
            BuildSandboxInteracHtml(
                request,
                workspace);

        var plainTextBody =
            BuildSandboxInteracPlainText(
                request,
                workspace);

        var internetMessageID =
            MimeUtils.GenerateMessageId(
                _sandboxEmailDomain);

        var rawMime =
            BuildSandboxMimeMessage(
                request,
                workspace,
                subject,
                internetMessageID,
                htmlBody,
                plainTextBody);

        var receivedDate =
            DateTimeOffset.UtcNow;

        var sourceMessage =
            new GraphEmailMessage
            {
                Id =
                    $"sandbox-direct-{Guid.NewGuid():N}",

                Subject =
                    subject,

                InternetMessageId =
                    internetMessageID,

                ReceivedDateTime =
                    receivedDate,

                FromAddress =
                    _acsEmailSender.Trim(),

                FromName =
                    "Interac e-Transfer",

                RecipientEmailAddress =
                    workspace.SandboxEmailAddress,

                ReplyToAddress =
                    request.SenderEmailAddress.Trim()
            };

        var ingestionResult =
            await _donationEmailIngestionService
                .IngestAsync(
                    sourceMessage,
                    rawMime,
                    cancellationToken);

        if (!ingestionResult.WasSuccessful)
        {
            throw new InvalidOperationException(
                "The test email was created, but EMA could not complete the sandbox donation workflow.");
        }

        var donorAcknowledgementResult =
            EmailDeliveryResult.NotRequested();

        try
        {
            var notificationResult =
                await _emailNotificationService
                    .SendDonationStepEmailsAsync(
                        sourceMessage,
                        ingestionResult,
                        cancellationToken);

            donorAcknowledgementResult =
                notificationResult.Donor;
        }
        catch (Exception exception)
        {
            // The donation is already safely ingested. A notification failure should
            // not cause the sandbox form to create the donation a second time.
            donorAcknowledgementResult =
                EmailDeliveryResult.Failed(
                    "Acknowledgement notification processing failed.");

            _logger.LogError(
                exception,
                "Sandbox donation {DonationID} was created, but acknowledgement notification processing failed.",
                ingestionResult.NewDonationID);
        }

        _logger.LogInformation(
            "Sandbox Interac-style email processed directly through EMA ingestion. " +
            "OrganizationID={OrganizationID}, SandboxRecipient={SandboxRecipient}, " +
            "DonationID={DonationID}, DonorID={DonorID}, WasDuplicate={WasDuplicate}, " +
            "DonorAcknowledgementStatus={DonorAcknowledgementStatus}.",
            workspace.SandboxOrganizationID,
            workspace.SandboxEmailAddress,
            ingestionResult.NewDonationID,
            ingestionResult.EMADonorID,
            ingestionResult.WasDuplicate,
            donorAcknowledgementResult.Status);

        return new SendSandboxTestEmailResponseDto
        {
            Message =
                BuildSandboxTestEmailMessage(
                    ingestionResult,
                    donorAcknowledgementResult)
        };
    }


    private static string BuildSandboxTestEmailMessage(
        DonationIngestionResult ingestionResult,
        EmailDeliveryResult donorAcknowledgementResult)
    {
        if (ingestionResult.WasDuplicate)
        {
            return
                "That test email was already processed. No duplicate donation was created.";
        }

        var donationMessage =
            $"Test donation {ingestionResult.NewDonationID} was created in your sandbox.";

        return donorAcknowledgementResult.Status switch
        {
            EmailDeliveryStatus.Queued =>
                $"{donationMessage} The donor acknowledgement email was queued.",

            EmailDeliveryStatus.Skipped =>
                $"{donationMessage} No donor acknowledgement email was sent: " +
                donorAcknowledgementResult.Message,

            EmailDeliveryStatus.Failed =>
                $"{donationMessage} The donor acknowledgement email could not be queued. " +
                "Check EmailSendLog for the failure details.",

            _ =>
                $"{donationMessage} No donor acknowledgement was requested for this donation step."
        };
    }

    public async Task<SandboxCleanupResultDto> DeleteExpiredSandboxesAsync(
        int? inactiveDays = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveInactiveDays =
            inactiveDays is > 0
                ? inactiveDays.Value
                : _activeDays;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new SqlCommand(
                "dbo.sp_SandboxDeleteExpired",
                connection)
            {
                CommandType =
                    CommandType.StoredProcedure,

                CommandTimeout =
                    300
            };

        command.Parameters.Add(
            "@InactiveDays",
            SqlDbType.Int)
            .Value = effectiveInactiveDays;

        var deletedAliases =
            new List<DeletedSandboxAlias>();

        await using (var reader =
            await command.ExecuteReaderAsync(
                cancellationToken))
        {
            while (await reader.ReadAsync(
                       cancellationToken))
            {
                deletedAliases.Add(
                    new DeletedSandboxAlias
                    {
                        OrganizationID =
                            Convert.ToInt32(
                                reader["SandboxOrganizationID"]),

                        UserID =
                            reader["SandboxUserID"] is DBNull
                                ? null
                                : Convert.ToInt32(
                                    reader["SandboxUserID"]),

                        EmailAddress =
                            reader["SandboxEmailAddress"]?.ToString()
                            ?? string.Empty
                    });
            }
        }

        foreach (var deletedAlias in deletedAliases)
        {
            if (string.IsNullOrWhiteSpace(
                    deletedAlias.EmailAddress))
            {
                continue;
            }

            try
            {
                await _emailAliasProvisioner
                    .RemoveAliasAsync(
                        deletedAlias.OrganizationID,
                        deletedAlias.EmailAddress,
                        cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Sandbox OrganizationID {OrganizationID} was deleted from SQL, " +
                    "but alias removal failed for {SandboxEmailAddress}.",
                    deletedAlias.OrganizationID,
                    deletedAlias.EmailAddress);
            }
        }

        return new SandboxCleanupResultDto
        {
            DeletedSandboxCount =
                deletedAliases.Count,

            RemovedSandboxEmailAddresses =
                deletedAliases
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(
                            item.EmailAddress))
                    .Select(item =>
                        item.EmailAddress)
                    .ToList()
        };
    }

    private async Task<SandboxWorkspaceSummaryDto> ExecuteCreateSandboxProcedureAsync(
        string clientIPAddress,
        string phoneNumber,
        string sandboxEmailAddress,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new SqlCommand(
                "dbo.sp_SandboxCreate",
                connection)
            {
                CommandType =
                    CommandType.StoredProcedure,

                CommandTimeout =
                    300
            };

        command.Parameters.Add(
            "@TemplateOrganizationID",
            SqlDbType.Int)
            .Value = _templateOrganizationID;

        command.Parameters.Add(
            "@TemplateUserID",
            SqlDbType.Int)
            .Value =
                _templateUserID.HasValue
                    ? (object)_templateUserID.Value
                    : DBNull.Value;

        command.Parameters.Add(
            "@PhoneNumber",
            SqlDbType.NVarChar,
            20)
            .Value = phoneNumber;

        command.Parameters.Add(
            "@SandboxIPAddress",
            SqlDbType.VarChar,
            50)
            .Value = clientIPAddress;

        command.Parameters.Add(
            "@SandboxEmail",
            SqlDbType.NVarChar,
            512)
            .Value = sandboxEmailAddress;

        command.Parameters.Add(
            "@CreatedByUserID",
            SqlDbType.Int)
            .Value = _createdByUserID;

        command.Parameters.Add(
            "@MaxTemplateDonations",
            SqlDbType.Int)
            .Value = _maxTemplateDonations;

        command.Parameters.Add(
            "@ActiveDays",
            SqlDbType.Int)
            .Value = _activeDays;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            throw new InvalidOperationException(
                "dbo.sp_SandboxCreate did not return the created sandbox.");
        }

        return ReadWorkspace(
            reader);
    }

    private async Task<SandboxWorkspaceSummaryDto?> GetCurrentSandboxAsync(
        SqlConnection connection,
        string clientIPAddress,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                o.EMAOrganizationID AS SandboxOrganizationID,
                u.UserID AS SandboxUserID,
                o.OrganizationName,
                u.PhoneNumber,
                o.SandboxEtransferEmail AS SandboxEmailAddress,
                o.DateTimeCreated,
                activity.LastDonationDate
            FROM dbo.[User] u
            INNER JOIN dbo.Organization o
                ON o.EMAOrganizationID =
                   u.SandboxOrganizationID
            OUTER APPLY
            (
                SELECT
                    MAX(d.DateTimeReceived) AS LastDonationDate
                FROM dbo.Donation d
                WHERE d.EMAOrganizationID =
                      o.EMAOrganizationID
            ) activity
            WHERE u.IsActive = 1
              AND u.SandboxOrganizationID IS NOT NULL
              AND u.SandboxIPAddress = @SandboxIPAddress
              AND ISNULL(o.OrganizationStatusID, -1) = 0
              AND COALESCE(
                    activity.LastDonationDate,
                    o.DateTimeCreated,
                    u.DateTimeCreated,
                    SYSUTCDATETIME()
                  ) >= DATEADD(
                        DAY,
                        -@ActiveDays,
                        SYSUTCDATETIME())
            ORDER BY
                COALESCE(
                    activity.LastDonationDate,
                    o.DateTimeCreated,
                    u.DateTimeCreated)
                DESC,
                u.UserID DESC;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@SandboxIPAddress",
            SqlDbType.VarChar,
            50)
            .Value = clientIPAddress;

        command.Parameters.Add(
            "@ActiveDays",
            SqlDbType.Int)
            .Value = _activeDays;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        return await reader.ReadAsync(
                cancellationToken)
            ? ReadWorkspace(
                reader)
            : null;
    }

    private async Task<int?> GetOwnedSandboxUserIDAsync(
        SqlConnection connection,
        int sandboxOrganizationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                u.UserID
            FROM dbo.[User] u
            INNER JOIN dbo.Organization o
                ON o.EMAOrganizationID =
                   u.SandboxOrganizationID
            WHERE u.IsActive = 1
              AND u.SandboxOrganizationID =
                  @SandboxOrganizationID
              AND ISNULL(o.OrganizationStatusID, -1) = 0
              AND COALESCE(
                    (
                        SELECT MAX(d.DateTimeReceived)
                        FROM dbo.Donation d
                        WHERE d.EMAOrganizationID =
                              o.EMAOrganizationID
                    ),
                    o.DateTimeCreated,
                    u.DateTimeCreated,
                    SYSUTCDATETIME()
                  ) >= DATEADD(
                        DAY,
                        -@ActiveDays,
                        SYSUTCDATETIME())
            ORDER BY
                u.UserID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@SandboxOrganizationID",
            SqlDbType.Int)
            .Value = sandboxOrganizationID;

        command.Parameters.Add(
            "@ActiveDays",
            SqlDbType.Int)
            .Value = _activeDays;

        var value =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return value is null ||
               value is DBNull
            ? null
            : Convert.ToInt32(
                value);
    }

    private static async Task<LoginResponseDto?> BuildLoginResponseAsync(
        SqlConnection connection,
        int organizationID,
        int userID,
        CancellationToken cancellationToken)
    {
        const string userSql = """
            SELECT
                u.UserID,
                u.UserName,
                u.EmailAddress,
                u.PhoneNumber,
                o.EMAOrganizationID,
                o.OrganizationName
            FROM dbo.[User] u
            INNER JOIN dbo.OrganizationUser ou
                ON ou.UserID =
                   u.UserID
            INNER JOIN dbo.Organization o
                ON o.EMAOrganizationID =
                   ou.EMAOrganizationID
            WHERE u.UserID = @UserID
              AND o.EMAOrganizationID = @OrganizationID
              AND u.SandboxOrganizationID =
                  o.EMAOrganizationID
              AND u.IsActive = 1
              AND ISNULL(o.OrganizationStatusID, -1) = 0;
            """;

        await using var userCommand =
            new SqlCommand(
                userSql,
                connection);

        userCommand.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        userCommand.Parameters.Add(
            "@OrganizationID",
            SqlDbType.Int)
            .Value = organizationID;

        LoginResponseDto response;

        await using (var reader =
            await userCommand.ExecuteReaderAsync(
                cancellationToken))
        {
            if (!await reader.ReadAsync(
                    cancellationToken))
            {
                return null;
            }

            response =
                new LoginResponseDto
                {
                    UserID =
                        Convert.ToInt32(
                            reader["UserID"]),

                    UserName =
                        reader["UserName"]?.ToString()
                        ?? string.Empty,

                    EmailAddress =
                        reader["EmailAddress"]?.ToString()
                        ?? string.Empty,

                    PhoneNumber =
                        reader["PhoneNumber"]?.ToString()
                        ?? string.Empty,

                    EMAOrganizationID =
                        Convert.ToInt32(
                            reader["EMAOrganizationID"]),

                    OrganizationName =
                        reader["OrganizationName"]?.ToString()
                        ?? string.Empty,

                    DefaultCategoryID =
                        OperationsCategoryID,

                    IsDonorSession =
                        false,

                    DonorID =
                        null
                };
        }

        response.Roles =
            await GetUserRolesAsync(
                connection,
                organizationID,
                userID,
                cancellationToken);

        response.AllowedCategoryIDs =
            await GetAllowedCategoryIDsAsync(
                connection,
                organizationID,
                userID,
                cancellationToken);

        if (!response.AllowedCategoryIDs.Contains(
                BrochureCategoryID))
        {
            response.AllowedCategoryIDs.Add(
                BrochureCategoryID);
        }

        response.AllowedCategoryIDs =
            response
                .AllowedCategoryIDs
                .Distinct()
                .OrderBy(categoryID =>
                    categoryID)
                .ToList();

        response.DefaultCategoryID =
            response.AllowedCategoryIDs.Contains(
                OperationsCategoryID)
                ? OperationsCategoryID
                : response.AllowedCategoryIDs.First();

        return response;
    }

    private static async Task<List<LoginRoleDto>> GetUserRolesAsync(
        SqlConnection connection,
        int organizationID,
        int userID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT
                r.RoleID,
                r.RoleName
            FROM dbo.OrganizationUserRole ur
            INNER JOIN dbo.[Role] r
                ON r.RoleID =
                   ur.RoleID
            WHERE ur.EMAOrganizationID =
                  @OrganizationID
              AND ur.UserID = @UserID
              AND ISNULL(r.IsActive, 'Y') = 'Y'
            ORDER BY
                r.RoleName;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@OrganizationID",
            SqlDbType.Int)
            .Value = organizationID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var roles =
            new List<LoginRoleDto>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            roles.Add(
                new LoginRoleDto
                {
                    RoleID =
                        Convert.ToInt32(
                            reader["RoleID"]),

                    RoleName =
                        reader["RoleName"]?.ToString()
                        ?? string.Empty
                });
        }

        return roles;
    }

    private static async Task<List<int>> GetAllowedCategoryIDsAsync(
        SqlConnection connection,
        int organizationID,
        int userID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT
                CONVERT(INT, mi.MenuItemCategoryID) AS CategoryID
            FROM dbo.OrganizationUserRole ur
            INNER JOIN dbo.RoleMenuItem rmi
                ON rmi.RoleID =
                   ur.RoleID
            INNER JOIN dbo.MenuItem mi
                ON mi.MenuItemID =
                   rmi.MenuItemID
            INNER JOIN dbo.[Role] r
                ON r.RoleID =
                   ur.RoleID
            WHERE ur.EMAOrganizationID =
                  @OrganizationID
              AND ur.UserID = @UserID
              AND mi.MenuItemCategoryID IS NOT NULL
              AND ISNULL(mi.IsActive, 'Y') = 'Y'
              AND ISNULL(r.IsActive, 'Y') = 'Y'
            ORDER BY
                CategoryID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@OrganizationID",
            SqlDbType.Int)
            .Value = organizationID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var categoryIDs =
            new List<int>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            categoryIDs.Add(
                Convert.ToInt32(
                    reader["CategoryID"]));
        }

        return categoryIDs;
    }

    private static SandboxWorkspaceSummaryDto ReadWorkspace(
        SqlDataReader reader)
    {
        return new SandboxWorkspaceSummaryDto
        {
            SandboxOrganizationID =
                Convert.ToInt32(
                    reader["SandboxOrganizationID"]),

            SandboxUserID =
                Convert.ToInt32(
                    reader["SandboxUserID"]),

            OrganizationName =
                reader["OrganizationName"]?.ToString()
                ?? string.Empty,

            PhoneNumber =
                reader["PhoneNumber"]?.ToString()
                ?? string.Empty,

            SandboxEmailAddress =
                reader["SandboxEmailAddress"]?.ToString()
                ?? string.Empty,

            DateTimeCreated =
                GetNullableDateTime(
                    reader,
                    "DateTimeCreated"),

            LastDonationDate =
                GetNullableDateTime(
                    reader,
                    "LastDonationDate")
        };
    }

    private static DateTime? GetNullableDateTime(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(
            ordinal)
            ? null
            : Convert.ToDateTime(
                reader.GetValue(
                    ordinal),
                CultureInfo.InvariantCulture);
    }

    private static void ValidateTestEmailRequest(
        SendSandboxTestEmailRequestDto request)
    {
        if (request.SandboxOrganizationID <= 0)
        {
            throw new ArgumentException(
                "A sandbox organization is required.");
        }

        if (string.IsNullOrWhiteSpace(
                request.SenderName))
        {
            throw new ArgumentException(
                "Please enter a sender name.");
        }

        if (!IsValidEmailAddress(
                request.SenderEmailAddress))
        {
            throw new ArgumentException(
                "Please enter a valid sender email address.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException(
                "The amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(
                request.ReferenceNumber))
        {
            throw new ArgumentException(
                "Please enter a reference number.");
        }

        if (request.SenderName.Length > 255 ||
            request.SenderEmailAddress.Length > 255 ||
            request.ReferenceNumber.Length > 255 ||
            (request.Message?.Length ?? 0) > 2000)
        {
            throw new ArgumentException(
                "One or more test email fields are too long.");
        }
    }

    private static bool IsValidEmailAddress(
        string? emailAddress)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            return false;
        }

        try
        {
            var parsed =
                new MailAddress(
                    emailAddress.Trim());

            return string.Equals(
                parsed.Address,
                emailAddress.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private byte[] BuildSandboxMimeMessage(
        SendSandboxTestEmailRequestDto request,
        SandboxWorkspaceSummaryDto workspace,
        string subject,
        string internetMessageID,
        string htmlBody,
        string plainTextBody)
    {
        var message =
            new MimeMessage();

        message.From.Add(
            new MailboxAddress(
                "Interac e-Transfer",
                _acsEmailSender!.Trim()));

        message.To.Add(
            new MailboxAddress(
                workspace.OrganizationName,
                workspace.SandboxEmailAddress));

        message.ReplyTo.Add(
            new MailboxAddress(
                request.SenderName.Trim(),
                request.SenderEmailAddress.Trim()));

        message.Subject =
            subject;

        message.MessageId =
            internetMessageID;

        message.Date =
            DateTimeOffset.UtcNow;

        message.Headers.Add(
            "X-EMA-Sandbox-Organization-ID",
            workspace.SandboxOrganizationID.ToString(
                CultureInfo.InvariantCulture));

        message.Headers.Add(
            "X-EMA-Sandbox-Email",
            workspace.SandboxEmailAddress);

        message.Headers.Add(
            "X-PaymentKey",
            request.ReferenceNumber.Trim());

        message.Headers.Add(
            "X-Payment-Notification",
            $"sandbox-{workspace.SandboxOrganizationID}-{Guid.NewGuid():N}");

        var bodyBuilder =
            new BodyBuilder
            {
                TextBody =
                    plainTextBody,

                HtmlBody =
                    htmlBody
            };

        message.Body =
            bodyBuilder.ToMessageBody();

        using var stream =
            new MemoryStream();

        message.WriteTo(
            stream);

        return stream.ToArray();
    }

    private static string BuildSandboxInteracHtml(
        SendSandboxTestEmailRequestDto request,
        SandboxWorkspaceSummaryDto workspace)
    {
        var senderName =
            WebUtility.HtmlEncode(
                request.SenderName.Trim());

        var senderEmail =
            WebUtility.HtmlEncode(
                request.SenderEmailAddress.Trim());

        var referenceNumber =
            WebUtility.HtmlEncode(
                request.ReferenceNumber.Trim());

        var message =
            WebUtility.HtmlEncode(
                request.Message?.Trim()
                ?? string.Empty);

        var organizationName =
            WebUtility.HtmlEncode(
                workspace.OrganizationName);

        return
            $$"""
            <!doctype html>
            <html>
            <body style="margin:0;background:#f3f4f6;font-family:Arial,Helvetica,sans-serif;color:#17202a;">
                <div style="max-width:680px;margin:24px auto;background:#ffffff;border:1px solid #c7c7c7;">
                    <div style="padding:18px 22px;background:#f7f7f7;border-bottom:1px solid #c7c7c7;font-weight:700;">
                        INTERAC e-Transfer: {{senderName}} sent you money.
                    </div>

                    <div style="padding:24px;">
                        <p>Hi {{organizationName}},</p>

                        <p>
                            <strong>{{senderName}}</strong> has sent you
                            <strong>{{request.Amount.ToString("C2", CultureInfo.GetCultureInfo("en-CA"))}}</strong>
                            (CAD), and the money has been automatically deposited into your account at
                            EMA Sandbox Bank.
                        </p>

                        <p><strong>Sandbox Organization ID:</strong> {{workspace.SandboxOrganizationID}}</p>
                        <p><strong>Sandbox Email:</strong> {{WebUtility.HtmlEncode(workspace.SandboxEmailAddress)}}</p>
                        <p><strong>Sent From:</strong> {{senderName}}</p>
                        <p><strong>Donor Email:</strong> {{senderEmail}}</p>
                        <p><strong>Amount:</strong> ${{request.Amount.ToString("0.00", CultureInfo.InvariantCulture)}}</p>
                        <p><strong>Reference Number:</strong> {{referenceNumber}}</p>
                        <p><strong>Message:</strong> {{message}}</p>

                        <p style="margin-top:30px;font-size:13px;color:#5f6368;">
                            This message was generated by the EMA sandbox test-email screen.
                        </p>
                    </div>
                </div>
            </body>
            </html>
            """;
    }

    private static string BuildSandboxInteracPlainText(
        SendSandboxTestEmailRequestDto request,
        SandboxWorkspaceSummaryDto workspace)
    {
        return
            $"""
            INTERAC e-Transfer: {request.SenderName.Trim()} sent you money.

            Hi {workspace.OrganizationName},

            {request.SenderName.Trim()} has sent you ${request.Amount:0.00} (CAD), and the money has been automatically deposited into your account at EMA Sandbox Bank.

            Sandbox Organization ID: {workspace.SandboxOrganizationID}
            Sandbox Email: {workspace.SandboxEmailAddress}
            Sent From: {request.SenderName.Trim()}
            Donor Email: {request.SenderEmailAddress.Trim()}
            Amount: ${request.Amount.ToString("0.00", CultureInfo.InvariantCulture)}
            Reference Number: {request.ReferenceNumber.Trim()}
            Message: {request.Message?.Trim() ?? string.Empty}

            This email was sent on behalf of {request.SenderName.Trim()} at EMA Sandbox Test Bank.
            """;
    }

    private static string GenerateSandboxPhoneNumber()
    {
        var firstDigit =
            RandomNumberGenerator.GetInt32(
                2,
                10);

        var remainingDigits =
            RandomNumberGenerator.GetInt32(
                0,
                1_000_000_000);

        return
            $"{firstDigit}{remainingDigits:D9}";
    }

    private static bool IsPhoneNumberCollision(
        SqlException exception)
    {
        return exception.Number is
            2601 or
            2627 or
            51004;
    }

    private static string NormalizeIPAddress(
        string clientIPAddress)
    {
        if (string.IsNullOrWhiteSpace(
                clientIPAddress) ||
            string.Equals(
                clientIPAddress.Trim(),
                "unknown",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The client IP address could not be determined.");
        }

        clientIPAddress =
            clientIPAddress.Trim();

        return clientIPAddress.Length <= 50
            ? clientIPAddress
            : clientIPAddress[..50];
    }

    private static string DigitsOnly(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        return new string(
            value
                .Where(
                    char.IsDigit)
                .ToArray());
    }

    private static int ParseRequiredPositiveInt(
        string? value,
        string settingName)
    {
        if (!int.TryParse(
                value,
                out var parsedValue) ||
            parsedValue <= 0)
        {
            throw new InvalidOperationException(
                $"Missing or invalid {settingName} setting.");
        }

        return parsedValue;
    }

    private static int? ParseOptionalPositiveInt(
        string? value)
    {
        return int.TryParse(
                   value,
                   out var parsedValue) &&
               parsedValue > 0
            ? parsedValue
            : null;
    }

    private sealed class DeletedSandboxAlias
    {
        public int OrganizationID { get; set; }

        public int? UserID { get; set; }

        public string EmailAddress { get; set; } = string.Empty;
    }
}
