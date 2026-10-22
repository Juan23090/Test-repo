using System.Data;
using EMA.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Email;

public sealed class EmailNotificationService : IEmailNotificationService
{
    private const int HoldingOrgID = 0;
    private const int NotInteracEmailStepID = 5;
    private const int DefaultFundAppliedStepID = 85;
    private const int AcknowledgementEmailSentStepID = 1000;

    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _emailTemplateService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly string _connectionString;

    public EmailNotificationService(
        IEmailService emailService,
        IEmailTemplateService emailTemplateService,
        IConfiguration configuration,
        ILogger<EmailNotificationService> logger)
    {
        _emailService = emailService;
        _emailTemplateService = emailTemplateService;
        _configuration = configuration;
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing 'SqlConnectionString' environment variable.");
    }

    public async Task<DonationStepEmailResult> SendDonationStepEmailsAsync(
        GraphEmailMessage sourceMessage,
        DonationIngestionResult ingestionResult,
        CancellationToken cancellationToken = default)
    {
        var donorResult =
            EmailDeliveryResult.NotRequested();

        var organizationResult =
            EmailDeliveryResult.NotRequested();

        var supportResult =
            EmailDeliveryResult.NotRequested();

        if (ingestionResult.DonationStepID is null ||
            ingestionResult.NewDonationID <= 0)
        {
            return new DonationStepEmailResult(
                donorResult,
                organizationResult,
                supportResult);
        }

        var context = await LoadEmailContextAsync(
            ingestionResult.NewDonationID,
            ingestionResult.DonationStepID.Value,
            sourceMessage.RecipientEmailAddress,
            cancellationToken);

        if (context is null)
        {
            _logger.LogWarning(
                "Could not load email context for DonationID={DonationID}, StepID={StepID}.",
                ingestionResult.NewDonationID,
                ingestionResult.DonationStepID);

            return new DonationStepEmailResult(
                EmailDeliveryResult.Failed(
                    "The donation email context could not be loaded."),
                organizationResult,
                supportResult);
        }

        var acknowledgementType =
            context.DonationStepID ==
            DefaultFundAppliedStepID
                ? DonationTransactionType.Automated
                : (DonationTransactionType?)null;

        if (context.NotifyDonor)
        {
            donorResult =
                await SendOneAsync(
                    recipientType: "Donor",
                    recipientEmail: ResolveDonorEmail(
                        sourceMessage,
                        context),
                    sourceMessage,
                    context,
                    acknowledgementType,
                    cancellationToken);
        }

        if (context.NotifyOrgAdmin)
        {
            organizationResult =
                await SendOneAsync(
                    recipientType: "OrgAdmin",
                    recipientEmail: ResolveOrgAdminEmail(
                        context),
                    sourceMessage,
                    context,
                    acknowledgementType: null,
                    cancellationToken: cancellationToken);
        }

        if (context.NotifyEMASupport)
        {
            supportResult =
                await SendOneAsync(
                    recipientType: "EMASupport",
                    recipientEmail: ResolveSupportEmail(),
                    sourceMessage,
                    context,
                    acknowledgementType: null,
                    cancellationToken: cancellationToken);
        }

        return new DonationStepEmailResult(
            donorResult,
            organizationResult,
            supportResult);
    }

    public async Task<EmailDeliveryResult> SendDonationAcknowledgementAsync(
        long donationID,
        DonationTransactionType transactionType,
        CancellationToken cancellationToken = default)
    {
        if (donationID <= 0)
        {
            return EmailDeliveryResult.Failed(
                "A valid donation is required.");
        }

        var context =
            await LoadEmailContextAsync(
                donationID,
                DefaultFundAppliedStepID,
                originalRecipientEmailAddress: null,
                cancellationToken: cancellationToken);

        if (context is null)
        {
            return EmailDeliveryResult.Failed(
                "The donation acknowledgement context could not be loaded.");
        }

        if (!context.NotifyDonor)
        {
            const string reason =
                "The acknowledgement donation step is not configured to notify donors.";

            await LogEmailAsync(
                context.EMADonationID,
                context.DonationStepID,
                "Donor",
                context.DonorEmail,
                null,
                null,
                null,
                "Skipped",
                reason,
                cancellationToken);

            return EmailDeliveryResult.Skipped(
                reason);
        }

        return await SendOneAsync(
            recipientType: "Donor",
            recipientEmail: ResolveDonorEmail(
                null,
                context),
            sourceMessage: null,
            context: context,
            acknowledgementType: transactionType,
            cancellationToken: cancellationToken);
    }

    private async Task<EmailDeliveryResult> SendOneAsync(
        string recipientType,
        string? recipientEmail,
        GraphEmailMessage? sourceMessage,
        EmailContext context,
        DonationTransactionType? acknowledgementType,
        CancellationToken cancellationToken)
    {
        if (acknowledgementType.HasValue &&
            string.Equals(
                recipientType,
                "Donor",
                StringComparison.OrdinalIgnoreCase))
        {
            var skipReason =
                GetAcknowledgementSkipReason(
                    context,
                    acknowledgementType.Value);

            if (skipReason is not null)
            {
                await LogEmailAsync(
                    context.EMADonationID,
                    context.DonationStepID,
                    recipientType,
                    recipientEmail,
                    null,
                    null,
                    null,
                    "Skipped",
                    skipReason,
                    cancellationToken);

                return EmailDeliveryResult.Skipped(
                    skipReason);
            }
        }

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            const string reason =
                "Recipient email could not be resolved.";

            await LogEmailAsync(
                context.EMADonationID,
                context.DonationStepID,
                recipientType,
                null,
                null,
                null,
                null,
                "Skipped",
                reason,
                cancellationToken);

            return EmailDeliveryResult.Skipped(
                reason);
        }

        if (await WasAlreadyQueuedAsync(
            context.EMADonationID,
            context.DonationStepID,
            recipientType,
            recipientEmail,
            cancellationToken))
        {
            if (context.DonationStepID ==
                    DefaultFundAppliedStepID &&
                string.Equals(
                    recipientType,
                    "Donor",
                    StringComparison.OrdinalIgnoreCase))
            {
                await LogDonationStepAsync(
                    context.EMADonationID,
                    AcknowledgementEmailSentStepID,
                    cancellationToken);
            }

            return EmailDeliveryResult.Queued(
                "The email was already queued.");
        }

        var template = await LoadTemplateAsync(
            context.DonationStepID,
            recipientType,
            cancellationToken);

        if (template is null)
        {
            var reason =
                $"No active template configured for DonationStepID={context.DonationStepID}, RecipientType={recipientType}.";

            await LogEmailAsync(
                context.EMADonationID,
                context.DonationStepID,
                recipientType,
                recipientEmail,
                null,
                null,
                null,
                "Skipped",
                reason,
                cancellationToken);

            return EmailDeliveryResult.Skipped(
                reason);
        }

        var senderAddress = _configuration["ACS_EMAIL_SENDER"]
            ?? throw new InvalidOperationException(
                "Missing ACS_EMAIL_SENDER setting.");

        var values = BuildTemplateValues(
            sourceMessage,
            context,
            recipientType,
            recipientEmail);

        var subject = BuildSubject(
            recipientType,
            context);

        var htmlBody = await _emailTemplateService.RenderTemplateAsync(
            template.TemplateFileName,
            values,
            cancellationToken);

        try
        {
            var operationId = await _emailService.SendHtmlEmailAsync(
                senderAddress: senderAddress,
                recipientAddress: recipientEmail,
                subject: subject,
                htmlBody: htmlBody,
                plainTextBody: BuildPlainText(
                    values),
                cancellationToken: cancellationToken);

            await LogEmailAsync(
                context.EMADonationID,
                context.DonationStepID,
                recipientType,
                recipientEmail,
                template.TemplateFileName,
                subject,
                operationId,
                "Queued",
                null,
                cancellationToken);

            if (context.DonationStepID ==
                    DefaultFundAppliedStepID &&
                string.Equals(
                    recipientType,
                    "Donor",
                    StringComparison.OrdinalIgnoreCase))
            {
                await LogDonationStepAsync(
                    context.EMADonationID,
                    AcknowledgementEmailSentStepID,
                    cancellationToken);
            }

            return EmailDeliveryResult.Queued();
        }
        catch (Exception ex)
        {
            await LogEmailAsync(
                context.EMADonationID,
                context.DonationStepID,
                recipientType,
                recipientEmail,
                template.TemplateFileName,
                subject,
                null,
                "Failed",
                ex.Message,
                cancellationToken);

            _logger.LogError(
                ex,
                "Failed to send {RecipientType} email for DonationID={DonationID}, StepID={StepID}.",
                recipientType,
                context.EMADonationID,
                context.DonationStepID);

            return EmailDeliveryResult.Failed(
                ex.Message);
        }
    }

    private string? ResolveDonorEmail(GraphEmailMessage? sourceMessage, EmailContext context)
    {
        if (context.DonationStepID == NotInteracEmailStepID)
        {
            return FirstNonEmpty(
                sourceMessage?.ReplyToAddress,
                sourceMessage?.FromAddress);
        }

        return FirstNonEmpty(
            context.DonorEmail,
            sourceMessage?.ReplyToAddress,
            sourceMessage?.FromAddress);
    }

    private static string? ResolveOrgAdminEmail(EmailContext context)
    {
        if (context.EffectiveOrganizationID <= HoldingOrgID)
        {
            return null;
        }

        return context.AdminEmail;
    }

    private string? ResolveSupportEmail()
    {
        return FirstNonEmpty(
            _configuration["EMA_SUPPORT_EMAIL"],
            _configuration["Email:SupportAddress"]);
    }

    private async Task<EmailContext?> LoadEmailContextAsync(
        long donationID,
        int donationStepID,
        string? originalRecipientEmailAddress,
        CancellationToken cancellationToken)
    {
        const string sql = @"
DECLARE @OriginalRecipientEmailAddress VARCHAR(255) = NULLIF(LOWER(LTRIM(RTRIM(@RecipientEmailAddress))), '');

SELECT TOP (1)
    d.EMADonationID,
    d.EMAOrganizationID AS DonationOrganizationID,
    d.EMADonorID,
    d.Comments,
    d.DateTimeReceived,
    d.DeadlineToMakeFundChanges,
    d.ReferenceNumber,
    d.DonationAmount,
    d.IsTemplate,
    d.RecipientEmailAddress,
    d.InteracDonorName AS DonationInteracDonorName,
    donor.EmailAddress AS DonorEmail,
    donor.OfficialDonorName,
    donor.InteracDonorName AS DonorInteracDonorName,
    donor.Address,
    donor.City,
    donor.ProvinceStateCode,
    donor.PostalCode,
    donor.PhoneNumber,
    donor.AcknowledgmentForManualTransactions,
    donor.AcknowledgementForAutomatedTransactions,
    methodOfGiving.AcknowledgeToDonor AS MethodAcknowledgeToDonor,
    CASE
    WHEN d.EMAOrganizationID > 0 THEN orgByDonation.EMAOrganizationID
    ELSE COALESCE(orgByRecipient.EMAOrganizationID, orgByDonation.EMAOrganizationID, 0)
END AS EffectiveOrganizationID,

CASE
    WHEN d.EMAOrganizationID > 0 THEN orgByDonation.OrganizationName
    ELSE COALESCE(orgByRecipient.OrganizationName, orgByDonation.OrganizationName, 'EMA Nonprofit Technologies')
END AS OrganizationName,

CASE
    WHEN d.EMAOrganizationID > 0 THEN orgByDonation.AdminEmail
    ELSE COALESCE(orgByRecipient.AdminEmail, orgByDonation.AdminEmail)
END AS AdminEmail,
    campus.Name AS CampusName,
    fund.Name AS FundName,
    distribution.DistributionAmount,
    ds.DonationStepID,
    ds.[Description] AS DonationStepDescription,
    ds.Explanation AS DonationStepExplanation,
    ds.NotifyDonor,
    ds.NotifyOrgAdmin,
    ds.NotifyEMASupport
FROM dbo.Donation d
LEFT JOIN dbo.Donor donor
    ON donor.EMADonorID = d.EMADonorID
   AND donor.EMAOrganizationID = d.EMAOrganizationID
LEFT JOIN dbo.Organization orgByDonation
    ON orgByDonation.EMAOrganizationID = d.EMAOrganizationID
LEFT JOIN dbo.OrganizationMethodOfGiving methodOfGiving
    ON methodOfGiving.EMAOrganizationID = d.EMAOrganizationID
   AND methodOfGiving.EMAMethodOfGivingID = d.EMAMethodOfGivingID
LEFT JOIN dbo.Organization orgByRecipient
    ON LOWER(LTRIM(RTRIM(orgByRecipient.EtransferEmailAddress))) = @OriginalRecipientEmailAddress
LEFT JOIN dbo.DonationStep ds
    ON ds.DonationStepID = @DonationStepID
OUTER APPLY
(
    SELECT TOP (1)
        dd.EMACampusID,
        dd.EMAFundID,
        dd.DistributionAmount
    FROM dbo.DonationDistribution dd
    WHERE dd.EMADonationID = d.EMADonationID
    ORDER BY dd.EMADonationDistributionID
) distribution
LEFT JOIN dbo.OrganizationCampus campus
    ON campus.EMACampusID = distribution.EMACampusID
LEFT JOIN dbo.OrganizationFund fund
    ON fund.EMAFundID = distribution.EMAFundID
WHERE d.EMADonationID = @DonationID;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@DonationID", SqlDbType.BigInt).Value = donationID;
        cmd.Parameters.Add("@DonationStepID", SqlDbType.Int).Value = donationStepID;
        cmd.Parameters.Add("@RecipientEmailAddress", SqlDbType.VarChar, 255).Value =
            string.IsNullOrWhiteSpace(originalRecipientEmailAddress)
                ? DBNull.Value
                : originalRecipientEmailAddress.Trim();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EmailContext
        {
            EMADonationID = GetInt64(reader, "EMADonationID"),
            DonationOrganizationID = GetInt32(reader, "DonationOrganizationID"),
            EffectiveOrganizationID = GetInt32(reader, "EffectiveOrganizationID"),
            EMADonorID = GetInt32(reader, "EMADonorID"),
            OrganizationName = GetString(reader, "OrganizationName"),
            AdminEmail = GetString(reader, "AdminEmail"),
            DonorEmail = GetString(reader, "DonorEmail"),
            OfficialDonorName = GetString(reader, "OfficialDonorName"),
            InteracDonorName = FirstNonEmpty(
                GetString(reader, "DonationInteracDonorName"),
                GetString(reader, "DonorInteracDonorName")),
            Address = GetString(reader, "Address"),
            City = GetString(reader, "City"),
            ProvinceStateCode = GetString(reader, "ProvinceStateCode"),
            PostalCode = GetString(reader, "PostalCode"),
            PhoneNumber = GetString(reader, "PhoneNumber"),
            AcknowledgementForManualTransactions =
                IsYes(
                    reader,
                    "AcknowledgmentForManualTransactions"),
            AcknowledgementForAutomatedTransactions =
                IsYes(
                    reader,
                    "AcknowledgementForAutomatedTransactions"),
            MethodAcknowledgeToDonor =
                IsYes(
                    reader,
                    "MethodAcknowledgeToDonor"),
            Comments = GetString(reader, "Comments"),
            DateTimeReceived = GetNullableDateTime(reader, "DateTimeReceived"),
            DeadlineToMakeFundChanges = GetNullableDateTime(reader, "DeadlineToMakeFundChanges"),
            ReferenceNumber = GetString(reader, "ReferenceNumber"),
            DonationAmount = GetNullableDecimal(reader, "DonationAmount"),
            IsTemplate = GetString(reader, "IsTemplate"),
            RecipientEmailAddress = GetString(reader, "RecipientEmailAddress"),
            CampusName = GetString(reader, "CampusName"),
            FundName = GetString(reader, "FundName"),
            DistributionAmount = GetNullableDecimal(reader, "DistributionAmount"),
            DonationStepID = GetInt32(reader, "DonationStepID"),
            DonationStepDescription = GetString(reader, "DonationStepDescription") ?? string.Empty,
            DonationStepExplanation = GetString(reader, "DonationStepExplanation"),
            NotifyDonor = IsYes(reader, "NotifyDonor"),
            NotifyOrgAdmin = IsYes(reader, "NotifyOrgAdmin"),
            NotifyEMASupport = IsYes(reader, "NotifyEMASupport")
        };
    }

    private async Task<EmailTemplateConfig?> LoadTemplateAsync(
        int donationStepID,
        string recipientType,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (1)
    TemplateFileName
FROM dbo.DonationStepEmailTemplate
WHERE DonationStepID = @DonationStepID
  AND RecipientType = @RecipientType
  AND IsActive = 'Y';";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@DonationStepID", SqlDbType.Int).Value = donationStepID;
        cmd.Parameters.Add("@RecipientType", SqlDbType.VarChar, 30).Value = recipientType;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EmailTemplateConfig
        {
            TemplateFileName = GetString(reader, "TemplateFileName") ?? string.Empty
        };
    }

    private async Task<bool> WasAlreadyQueuedAsync(
        long donationID,
        int donationStepID,
        string recipientType,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (1) 1
FROM dbo.EmailSendLog
WHERE EMADonationID = @DonationID
  AND DonationStepID = @DonationStepID
  AND RecipientType = @RecipientType
  AND RecipientEmail = @RecipientEmail
  AND Status = 'Queued';";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@DonationID", SqlDbType.BigInt).Value = donationID;
        cmd.Parameters.Add("@DonationStepID", SqlDbType.Int).Value = donationStepID;
        cmd.Parameters.Add("@RecipientType", SqlDbType.VarChar, 30).Value = recipientType;
        cmd.Parameters.Add("@RecipientEmail", SqlDbType.VarChar, 255).Value = recipientEmail;

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null && result is not DBNull;
    }

    private async Task LogEmailAsync(
        long donationID,
        int donationStepID,
        string recipientType,
        string? recipientEmail,
        string? templateFileName,
        string? subject,
        string? operationID,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT INTO dbo.EmailSendLog
(
    EMADonationID,
    DonationStepID,
    RecipientType,
    RecipientEmail,
    TemplateFileName,
    Subject,
    ProviderOperationID,
    Status,
    ErrorMessage
)
VALUES
(
    @DonationID,
    @DonationStepID,
    @RecipientType,
    @RecipientEmail,
    @TemplateFileName,
    @Subject,
    @OperationID,
    @Status,
    @ErrorMessage
);";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@DonationID", SqlDbType.BigInt).Value = donationID;
        cmd.Parameters.Add("@DonationStepID", SqlDbType.Int).Value = donationStepID;
        cmd.Parameters.Add("@RecipientType", SqlDbType.VarChar, 30).Value = recipientType;
        cmd.Parameters.Add("@RecipientEmail", SqlDbType.VarChar, 255).Value = ToDbValue(recipientEmail);
        cmd.Parameters.Add("@TemplateFileName", SqlDbType.VarChar, 255).Value = ToDbValue(templateFileName);
        cmd.Parameters.Add("@Subject", SqlDbType.NVarChar, 500).Value = ToDbValue(subject);
        cmd.Parameters.Add("@OperationID", SqlDbType.NVarChar, 255).Value = ToDbValue(operationID);
        cmd.Parameters.Add("@Status", SqlDbType.VarChar, 30).Value = status;
        cmd.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, -1).Value = ToDbValue(errorMessage);

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            _logger.LogInformation(
                "Duplicate queued email log skipped. DonationID={DonationID}, StepID={StepID}, RecipientType={RecipientType}, RecipientEmail={RecipientEmail}",
                donationID,
                donationStepID,
                recipientType,
                recipientEmail);
        }
    }

    private async Task LogDonationStepAsync(
        long donationID,
        int donationStepID,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = @DonationStepID
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStepLog WITH (UPDLOCK, HOLDLOCK)
    WHERE EMADonationID = @DonationID
      AND DonationStepID = @DonationStepID
)
BEGIN
    INSERT INTO dbo.DonationStepLog
    (
        EMADonationID,
        DonationStepID,
        DateTimeLogged
    )
    VALUES
    (
        @DonationID,
        @DonationStepID,
        SYSUTCDATETIME()
    );
END;

COMMIT TRANSACTION;";

        await using var conn =
            new SqlConnection(
                _connectionString);

        await conn.OpenAsync(
            cancellationToken);

        await using var cmd =
            new SqlCommand(
                sql,
                conn);

        cmd.Parameters.Add(
                "@DonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        cmd.Parameters.Add(
                "@DonationStepID",
                SqlDbType.Int)
            .Value =
                donationStepID;

        await cmd.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private Dictionary<string, string?> BuildTemplateValues(
        GraphEmailMessage? sourceMessage,
        EmailContext context,
        string recipientType,
        string recipientEmail)
    {
        var amount = context.DistributionAmount ?? context.DonationAmount;

        return new Dictionary<string, string?>
        {
            ["OrganizationName"] = context.OrganizationName,
            ["OfficialName"] = FirstNonEmpty(
                context.OfficialDonorName,
                context.InteracDonorName,
                sourceMessage?.FromName,
                "Unknown"),
            ["ETransferName"] = FirstNonEmpty(
                context.InteracDonorName,
                sourceMessage?.FromName,
                "Unknown"),
            ["Address"] = context.Address ?? string.Empty,
            ["CityTown"] = context.City ?? string.Empty,
            ["Province"] = context.ProvinceStateCode ?? string.Empty,
            ["PostalCode"] = context.PostalCode ?? string.Empty,
            ["DonorEmail"] = context.DonorEmail ?? string.Empty,
            ["DonorPhone"] = context.PhoneNumber ?? string.Empty,
            ["DonationReference"] = context.ReferenceNumber ?? string.Empty,
            ["DonationComments"] = context.Comments ?? string.Empty,
            ["DonationCommentsNote"] = context.DonationStepExplanation ?? string.Empty,
            ["DonationDeadlineToMakeFundChanges"] = context.DeadlineToMakeFundChanges?.ToString("MMMM d, yyyy") ?? string.Empty,
            ["CampusName"] = context.CampusName ?? string.Empty,
            ["FundName"] = context.FundName ?? string.Empty,
            ["DonationAmount"] = FormatMoney(amount),
            ["DonationTotal"] = FormatMoney(context.DonationAmount ?? amount),
            ["DefaultCheckmark"] = string.Equals(context.IsTemplate, "Y", StringComparison.OrdinalIgnoreCase) ? "✓" : string.Empty,
            // EmailTemplateService replaces both values with server-generated
            // donor-portal URLs before rendering.
            ["MakeChangesUrl"] = string.Empty,
            ["GivingHistoryUrl"] = string.Empty,
            ["FooterText"] = $"This email was sent by Email Money App on behalf of {context.OrganizationName}.",
            ["DonationStepID"] = context.DonationStepID.ToString(),
            ["DonationStepDescription"] = context.DonationStepDescription,
            ["DonationStepExplanation"] = context.DonationStepExplanation ?? string.Empty,
            ["EMADonationID"] = context.EMADonationID.ToString(),
            ["EMADonorID"] = context.EMADonorID.ToString(),
            ["EMAOrganizationID"] = context.EffectiveOrganizationID.ToString(),
            ["RecipientType"] = recipientType,
            ["RecipientEmailAddress"] = recipientEmail,
            ["OriginalEmailFrom"] = FirstNonEmpty(sourceMessage?.FromAddress, sourceMessage?.FromName, string.Empty),
            ["OriginalEmailFromName"] = sourceMessage?.FromName ?? string.Empty,
            ["OriginalEmailSubject"] = sourceMessage?.Subject ?? string.Empty,
            ["OriginalRecipientEmailAddress"] = sourceMessage?.RecipientEmailAddress ?? string.Empty
        };
    }

    private static string BuildPlainText(IReadOnlyDictionary<string, string?> values)
    {
        values.TryGetValue("OrganizationName", out var organizationName);
        values.TryGetValue("DonationStepDescription", out var stepDescription);
        values.TryGetValue("DonationStepExplanation", out var explanation);
        values.TryGetValue("DonationReference", out var reference);
        values.TryGetValue("DonationTotal", out var total);

        return $"""
{organizationName}

{stepDescription}

{explanation}

Reference: {reference}
Amount: {total}

This email was sent by Email Money App.
""";
    }

    internal static string? GetAcknowledgementSkipReason(
        bool methodAcknowledgesDonor,
        bool manualAcknowledgementsEnabled,
        bool automatedAcknowledgementsEnabled,
        DonationTransactionType transactionType)
    {
        if (!methodAcknowledgesDonor)
        {
            return "The donation's giving method is not configured to acknowledge the donor.";
        }

        var donorAllowsAcknowledgement =
            transactionType ==
                DonationTransactionType.Manual
                ? manualAcknowledgementsEnabled
                : automatedAcknowledgementsEnabled;

        return donorAllowsAcknowledgement
            ? null
            : transactionType ==
                DonationTransactionType.Manual
                ? "The donor has disabled manual transaction acknowledgements."
                : "The donor has disabled automated transaction acknowledgements.";
    }

    private static string? GetAcknowledgementSkipReason(
        EmailContext context,
        DonationTransactionType transactionType) =>
        GetAcknowledgementSkipReason(
            context.MethodAcknowledgeToDonor,
            context.AcknowledgementForManualTransactions,
            context.AcknowledgementForAutomatedTransactions,
            transactionType);

    private static bool IsYes(SqlDataReader reader, string columnName)
    {
        var value = GetString(reader, columnName);
        return string.Equals(value?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMoney(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("C") : string.Empty;
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? GetString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int GetInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static long GetInt64(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static string BuildSubject(
    string recipientType,
    EmailContext context)
    {
        if (string.Equals(recipientType, "Donor", StringComparison.OrdinalIgnoreCase))
        {
            return context.DonationStepID switch
            {
                5 => $"Your email to {context.OrganizationName} could not be processed",
                85 => $"Your donation to {context.OrganizationName} has been received",
                _ => $"Donation notification from {context.OrganizationName}"
            };
        }

        if (string.Equals(recipientType, "OrgAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return $"Donation notice: {context.DonationStepDescription}";
        }

        if (string.Equals(recipientType, "EMASupport", StringComparison.OrdinalIgnoreCase))
        {
            return $"EMA support notice: {context.DonationStepDescription}";
        }

        return context.DonationStepDescription;
    }

    private sealed class EmailTemplateConfig
    {
        public string TemplateFileName { get; init; } = string.Empty;
    }

    private sealed class EmailContext
    {
        public long EMADonationID { get; init; }
        public int DonationOrganizationID { get; init; }
        public int EffectiveOrganizationID { get; init; }
        public int EMADonorID { get; init; }
        public string OrganizationName { get; init; } = string.Empty;
        public string? AdminEmail { get; init; }
        public string? DonorEmail { get; init; }
        public string? OfficialDonorName { get; init; }
        public string? InteracDonorName { get; init; }
        public string? Address { get; init; }
        public string? City { get; init; }
        public string? ProvinceStateCode { get; init; }
        public string? PostalCode { get; init; }
        public string? PhoneNumber { get; init; }
        public bool AcknowledgementForManualTransactions { get; init; }
        public bool AcknowledgementForAutomatedTransactions { get; init; }
        public bool MethodAcknowledgeToDonor { get; init; }
        public string? Comments { get; init; }
        public DateTime? DateTimeReceived { get; init; }
        public DateTime? DeadlineToMakeFundChanges { get; init; }
        public string? ReferenceNumber { get; init; }
        public decimal? DonationAmount { get; init; }
        public string? IsTemplate { get; init; }
        public string? RecipientEmailAddress { get; init; }
        public string? CampusName { get; init; }
        public string? FundName { get; init; }
        public decimal? DistributionAmount { get; init; }
        public int DonationStepID { get; init; }
        public string DonationStepDescription { get; init; } = string.Empty;
        public string? DonationStepExplanation { get; init; }
        public bool NotifyDonor { get; init; }
        public bool NotifyOrgAdmin { get; init; }
        public bool NotifyEMASupport { get; init; }
    }
}
