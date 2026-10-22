using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using EMA.Api.Models;
using EMA.Api.Services.Email;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Referrals;

public sealed class ReferralService : IReferralService
{
    /// <summary>
    /// Upper bound on recipients accepted in a single request. Each recipient
    /// becomes an unsolicited email to a third party, so the batch size is
    /// capped rather than left to the 1 MB request body limit.
    /// </summary>
    public const int MaxRecipientsPerBatch = 50;

    private readonly string _connectionString;
    private readonly string _senderEmail;
    private readonly IEmailService _emailService;
    private readonly ILogger<ReferralService> _logger;

    public ReferralService(
        IConfiguration configuration,
        IEmailService emailService,
        ILogger<ReferralService> logger)
    {
        _emailService = emailService;
        _logger = logger;

        _connectionString = configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString is missing.");

        _senderEmail = configuration["ACS_EMAIL_SENDER"]
            ?? Environment.GetEnvironmentVariable("ACS_EMAIL_SENDER")
            ?? "notifications@emailmoney.app";
    }

    public async Task<ReferralEligibilityDto> GetEligibilityAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        var result = new ReferralEligibilityDto();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // 1. Call eligibility check stored procedure
        await using (var cmd = new SqlCommand("sp_Referral_CheckEligibility", conn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@EMAOrganizationID", orgID);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                result.IsEligible = reader["IsEligible"] is bool isEligible && isEligible;
                result.Reason = reader["Reason"]?.ToString() ?? string.Empty;
                result.DaysActive = reader["DaysActive"] is int days ? days : 0;
                result.DonationCount = reader["DonationCount"] is int count ? count : 0;
            }
        }

        // 2. Fetch default preface and email body from SystemSettings. These
        //    column names are a contract with InstallReferralProgramSchema.sql.
        const string templateSql = """
            SELECT TOP (1) ReferralEmailPrefaceDefault, ReferralEmailBodyDefault
            FROM dbo.SystemSettings
            ORDER BY SettingID DESC;
            """;

        await using (var cmd = new SqlCommand(templateSql, conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                result.DefaultPreface = reader["ReferralEmailPrefaceDefault"]?.ToString() ?? string.Empty;
                result.EmailBody = reader["ReferralEmailBodyDefault"]?.ToString() ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(result.DefaultPreface))
        {
            result.DefaultPreface = DefaultPrefaceText;
        }

        if (string.IsNullOrWhiteSpace(result.EmailBody))
        {
            result.EmailBody = DefaultEmailBodyText;
        }

        return result;
    }

    public async Task<SendReferralsResult> SendBatchReferralsAsync(
        int orgID,
        int userID,
        SendReferralsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new SendReferralsResult
        {
            TotalRequested = request.Recipients.Count
        };

        if (request.Recipients.Count == 0)
        {
            return result;
        }

        if (request.Recipients.Count > MaxRecipientsPerBatch)
        {
            return RejectEntireBatch(
                result,
                request,
                $"A single submission is limited to {MaxRecipientsPerBatch} recipients. Please send the remaining invitations in another batch.");
        }

        // 1. Check eligibility
        var eligibility = await GetEligibilityAsync(orgID, cancellationToken);
        if (!eligibility.IsEligible)
        {
            return RejectEntireBatch(
                result,
                request,
                $"Organization is not eligible to send referrals: {eligibility.Reason}");
        }

        // 2. Fetch referring organization details
        string referringOrgName = "A partner non-profit";
        string referringAdminEmail = string.Empty;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using (var cmd = new SqlCommand(
            "SELECT OrganizationName, AdminEmail FROM dbo.Organization WHERE EMAOrganizationID = @OrgID", conn))
        {
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                referringOrgName = reader["OrganizationName"]?.ToString() ?? referringOrgName;
                referringAdminEmail = reader["AdminEmail"]?.ToString() ?? string.Empty;
            }
        }

        // 3. Process each recipient
        var processedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipient in request.Recipients)
        {
            var itemResult = new SendReferralItemResult
            {
                EmailAddress = recipient.EmailAddress?.Trim() ?? string.Empty,
                OrganizationName = recipient.OrganizationName?.Trim() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(itemResult.OrganizationName))
            {
                AddFailure(result, itemResult, "Organization name is required.");
                continue;
            }

            // Syntax validation
            if (!IsValidEmail(itemResult.EmailAddress))
            {
                AddFailure(result, itemResult, "Invalid email format.");
                continue;
            }

            // Prevent self-referral
            if (!string.IsNullOrWhiteSpace(referringAdminEmail) &&
                string.Equals(itemResult.EmailAddress, referringAdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                AddFailure(result, itemResult, "Cannot refer your own organization's email address.");
                continue;
            }

            // Duplicate check in batch
            if (!processedEmails.Add(itemResult.EmailAddress))
            {
                AddFailure(result, itemResult, "Duplicate email in submission batch.");
                continue;
            }

            long referralId;
            string referralCode;

            try
            {
                // Record the invitation as Queued before dispatching, so a row
                // never claims an email was sent that the provider rejected.
                (referralId, referralCode) = await InsertQueuedReferralAsync(
                    conn,
                    orgID,
                    userID,
                    itemResult.OrganizationName,
                    recipient.ContactName,
                    itemResult.EmailAddress,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record referral for {Email}", itemResult.EmailAddress);
                AddFailure(result, itemResult, "Failed to record the referral invitation.");
                continue;
            }

            itemResult.ReferralID = referralId;

            try
            {
                var htmlBody = FormatReferralHtmlEmail(
                    referringOrgName,
                    recipient.ContactName,
                    request.PrefaceText,
                    eligibility.EmailBody,
                    referralCode,
                    itemResult.EmailAddress);

                var subject = $"{referringOrgName} has invited you to explore Email Money App";

                await _emailService.SendHtmlEmailAsync(
                    _senderEmail,
                    itemResult.EmailAddress,
                    subject,
                    htmlBody,
                    cancellationToken: cancellationToken);

                await SetReferralStatusAsync(conn, referralId, "Sent", stampSentDate: true, cancellationToken);

                itemResult.IsSuccess = true;
                result.SentCount++;
                result.Results.Add(itemResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send referral email to {Email}", itemResult.EmailAddress);

                await TrySetReferralFailedAsync(conn, referralId, cancellationToken);

                AddFailure(result, itemResult, "The invitation could not be delivered. Please verify the address and try again.");
            }
        }

        return result;
    }

    public async Task<AcceptReferralResult> AcceptReferralAsync(
        int referredOrgID,
        int userID,
        string referralCode,
        string referralEmail,
        CancellationToken cancellationToken = default)
    {
        var cleanCode = referralCode?.Trim() ?? string.Empty;
        var cleanEmail = referralEmail?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cleanCode))
        {
            return new AcceptReferralResult
            {
                IsSuccess = false,
                Message = "Please provide a valid referral code."
            };
        }

        if (!IsValidEmail(cleanEmail))
        {
            return new AcceptReferralResult
            {
                IsSuccess = false,
                Message = "Please provide a valid referral email address."
            };
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("sp_Referral_Accept", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@ReferralCode", cleanCode);
        cmd.Parameters.AddWithValue("@ReferredEmail", cleanEmail);
        cmd.Parameters.AddWithValue("@ReferredOrgID", referredOrgID);
        cmd.Parameters.AddWithValue("@AcceptedByUserID", userID);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var status = reader["Status"]?.ToString() ?? string.Empty;
            var message = reader["Message"]?.ToString() ?? string.Empty;
            long? referralId = reader["ReferralID"] != DBNull.Value ? Convert.ToInt64(reader["ReferralID"]) : null;
            int? referringOrgId = reader["ReferringOrgID"] != DBNull.Value ? Convert.ToInt32(reader["ReferringOrgID"]) : null;
            string? referringOrgName = reader["ReferringOrgName"]?.ToString();

            return new AcceptReferralResult
            {
                IsSuccess = string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase),
                Status = status,
                Message = message,
                ReferralID = referralId,
                ReferringOrgID = referringOrgId,
                ReferringOrgName = referringOrgName
            };
        }

        return new AcceptReferralResult
        {
            IsSuccess = false,
            Message = "No active referral found matching this referral code and email address."
        };
    }

    public async Task<ReferralSummaryDto> GetReferralHistoryAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        var summary = new ReferralSummaryDto();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Fetch balance
        await using (var cmd = new SqlCommand(
            "SELECT ISNULL(ReferralCreditBalance, 0) FROM dbo.Organization WHERE EMAOrganizationID = @OrgID", conn))
        {
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            var balanceObj = await cmd.ExecuteScalarAsync(cancellationToken);
            summary.AccumulatedCreditBalance = balanceObj is decimal bal ? bal : Convert.ToDecimal(balanceObj ?? 0);
        }

        // Fetch history via SP. Every column read here is produced by
        // dbo.sp_Referral_GetHistory in 20260901_ReferralStoredProcedures.sql.
        await using (var cmd = new SqlCommand("sp_Referral_GetHistory", conn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@EMAOrganizationID", orgID);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = new ReferralHistoryItemDto
                {
                    ReferralID = Convert.ToInt64(reader["ReferralID"]),
                    IsReferrer = reader["IsReferrer"] is bool isReferrer && isReferrer,
                    DateReferred = Convert.ToDateTime(reader["DateReferred"]),
                    ReferringOrganization = reader["ReferringOrganization"]?.ToString() ?? string.Empty,
                    ReferredByUser = reader["ReferredByUser"]?.ToString() ?? string.Empty,
                    ReferredTo = reader["ReferredTo"]?.ToString() ?? string.Empty,
                    EmailAddress = reader["EmailAddress"]?.ToString() ?? string.Empty,
                    RecipientOrganization = reader["RecipientOrganization"]?.ToString() ?? string.Empty,
                    RecipientUser = reader["RecipientUser"]?.ToString() ?? string.Empty,
                    DateAccepted = reader["DateAccepted"] != DBNull.Value ? Convert.ToDateTime(reader["DateAccepted"]) : null,
                    DateCredited = reader["DateCredited"] != DBNull.Value ? Convert.ToDateTime(reader["DateCredited"]) : null,
                    ReferringAmount = reader["ReferringAmount"] != DBNull.Value ? Convert.ToDecimal(reader["ReferringAmount"]) : null,
                    RecipientAmount = reader["RecipientAmount"] != DBNull.Value ? Convert.ToDecimal(reader["RecipientAmount"]) : null,
                    CreditAmount = reader["CreditAmount"] != DBNull.Value ? Convert.ToDecimal(reader["CreditAmount"]) : null,
                    Status = reader["Status"]?.ToString() ?? "Queued"
                };

                summary.History.Add(item);
            }
        }

        // The totals describe this organization's own outbound referral
        // programme, so referrals it received are excluded from the counts.
        var sent = summary.History.Where(item => item.IsReferrer).ToList();

        summary.TotalSent = sent.Count;

        summary.TotalAccepted = sent.Count(item =>
            string.Equals(item.Status, "Accepted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Status, "Credited", StringComparison.OrdinalIgnoreCase));

        summary.TotalCredited = sent.Count(item =>
            string.Equals(item.Status, "Credited", StringComparison.OrdinalIgnoreCase));

        return summary;
    }

    private static SendReferralsResult RejectEntireBatch(
        SendReferralsResult result,
        SendReferralsRequest request,
        string errorMessage)
    {
        foreach (var recipient in request.Recipients)
        {
            result.Results.Add(new SendReferralItemResult
            {
                EmailAddress = recipient.EmailAddress?.Trim() ?? string.Empty,
                OrganizationName = recipient.OrganizationName?.Trim() ?? string.Empty,
                IsSuccess = false,
                ErrorMessage = errorMessage
            });
        }

        result.FailedCount = request.Recipients.Count;
        return result;
    }

    private static void AddFailure(
        SendReferralsResult result,
        SendReferralItemResult itemResult,
        string errorMessage)
    {
        itemResult.IsSuccess = false;
        itemResult.ErrorMessage = errorMessage;
        result.Results.Add(itemResult);
        result.FailedCount++;
    }

    private static async Task<(long ReferralID, string ReferralCode)> InsertQueuedReferralAsync(
        SqlConnection conn,
        int orgID,
        int userID,
        string organizationName,
        string? contactName,
        string emailAddress,
        CancellationToken cancellationToken)
    {
        var referralCode = GenerateReferralCode();
        const string insertSql = """
            INSERT INTO dbo.Referral (
                ReferringOrgID,
                ReferredOrgName,
                ContactName,
                ReferredEmail,
                ReferralCode,
                Status,
                EmailSentDate,
                CreatedByUserID
            )
            OUTPUT INSERTED.ReferralID
            VALUES (
                @ReferringOrgID,
                @ReferredOrgName,
                @ContactName,
                @ReferredEmail,
                @ReferralCode,
                'Queued',
                SYSUTCDATETIME(),
                @CreatedByUserID
            );
            """;

        await using var insertCmd = new SqlCommand(insertSql, conn);
        insertCmd.Parameters.AddWithValue("@ReferringOrgID", orgID);
        insertCmd.Parameters.AddWithValue("@ReferredOrgName", organizationName);
        insertCmd.Parameters.AddWithValue(
            "@ContactName",
            string.IsNullOrWhiteSpace(contactName) ? DBNull.Value : contactName.Trim());
        insertCmd.Parameters.AddWithValue("@ReferredEmail", emailAddress);
        insertCmd.Parameters.AddWithValue("@ReferralCode", referralCode);
        insertCmd.Parameters.AddWithValue("@CreatedByUserID", userID);

        var idObj = await insertCmd.ExecuteScalarAsync(cancellationToken);
        return (Convert.ToInt64(idObj), referralCode);
    }

    private static async Task SetReferralStatusAsync(
        SqlConnection conn,
        long referralId,
        string status,
        bool stampSentDate,
        CancellationToken cancellationToken)
    {
        var sql = stampSentDate
            ? "UPDATE dbo.Referral SET Status = @Status, EmailSentDate = SYSUTCDATETIME() WHERE ReferralID = @ReferralID;"
            : "UPDATE dbo.Referral SET Status = @Status WHERE ReferralID = @ReferralID;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@ReferralID", referralId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TrySetReferralFailedAsync(
        SqlConnection conn,
        long referralId,
        CancellationToken cancellationToken)
    {
        try
        {
            await SetReferralStatusAsync(conn, referralId, "Failed", stampSentDate: false, cancellationToken);
        }
        catch (Exception ex)
        {
            // The invitation is already reported as failed to the caller. A row
            // stranded in Queued is not claimable, so this is safe to log only.
            _logger.LogError(ex, "Failed to mark referral {ReferralID} as Failed", referralId);
        }
    }

    private static string FormatReferralHtmlEmail(
        string referringOrgName,
        string? contactName,
        string? prefaceText,
        string? standardBody,
        string referralCode,
        string recipientEmail)
    {
        var greeting = string.IsNullOrWhiteSpace(contactName) ? "Hello," : $"Hello {contactName},";
        var safeGreeting = System.Net.WebUtility.HtmlEncode(greeting);
        var safePreface = System.Net.WebUtility.HtmlEncode(prefaceText ?? string.Empty).Replace("\n", "<br/>");
        var safeBody = System.Net.WebUtility.HtmlEncode(standardBody ?? string.Empty).Replace("\n", "<br/>");
        var encodedCode = Uri.EscapeDataString(referralCode);
        var encodedEmail = Uri.EscapeDataString(recipientEmail);
        var prefaceHtml = string.IsNullOrWhiteSpace(safePreface)
            ? string.Empty
            : $@"<div class=""preface-box"">""{safePreface}""</div>";

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; color: #1e293b; background-color: #f8fafc; margin: 0; padding: 24px; }}
        .email-container {{ max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #e2e8f0; overflow: hidden; }}
        .header {{ background-color: #1e3a8a; padding: 24px; text-align: center; color: #ffffff; }}
        .header h1 {{ margin: 0; font-size: 22px; font-weight: 700; }}
        .content {{ padding: 32px 24px; }}
        .greeting {{ font-size: 16px; font-weight: 600; margin-bottom: 16px; }}
        .preface-box {{ background-color: #f1f5f9; border-left: 4px solid #3b82f6; padding: 16px; margin: 20px 0; border-radius: 4px; font-style: italic; color: #1e293b; }}
        .cta-button {{ display: inline-block; background-color: #16a34a; color: #ffffff !important; padding: 12px 28px; border-radius: 6px; text-decoration: none; font-weight: 700; margin: 24px 0 16px; text-align: center; }}
        .footer {{ background-color: #f8fafc; padding: 20px 24px; text-align: center; font-size: 13px; color: #64748b; border-top: 1px solid #e2e8f0; }}
    </style>
</head>
<body>
    <div class=""email-container"">
        <div class=""header"">
            <h1>Email Money App (EMA)</h1>
        </div>
        <div class=""content"">
            <div class=""greeting"">{safeGreeting}</div>

            <p><strong>{System.Net.WebUtility.HtmlEncode(referringOrgName)}</strong> has invited your organization to explore Email Money App.</p>

            {prefaceHtml}

            <div style=""margin: 20px 0;"">
                {safeBody}
            </div>

            <div style=""text-align: center;"">
                <a href=""https://emailmoney.app/organization-signup?referralCode={encodedCode}&amp;email={encodedEmail}"" class=""cta-button"">Click here to see what the buzz is about</a>
            </div>

            <p style=""font-size: 13px; color: #475569; text-align: center;"">
                Already registered with EMA? Claim your referral credit at
                <a href=""https://emailmoney.app/organization/accept-referral?code={encodedCode}&amp;email={encodedEmail}"" style=""color: #2563eb;"">emailmoney.app/organization/accept-referral</a>.
            </p>

            <p style=""font-size: 14px; color: #475569;"">
                Want to try EMA risk-free first? Explore our 30-day sandbox with no credit card or commitment required at
                <a href=""https://emailmoney.app/user-sandbox"" style=""color: #2563eb;"">emailmoney.app/user-sandbox</a>.
            </p>
        </div>
        <div class=""footer"">
            <p>Sent on behalf of {System.Net.WebUtility.HtmlEncode(referringOrgName)} via Email Money App.</p>
            <p>This single email was sent because your peer organization requested permission to introduce EMA. We do not add your address to marketing lists.</p>
        </div>
    </div>
</body>
</html>";
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
            return false;

        try
        {
            var addr = new MailAddress(email);
            return string.Equals(addr.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateReferralCode()
    {
        // This value is the bearer secret used to claim an invitation. 256 bits
        // prevents practical online guessing even if a code is exposed in logs.
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private const string DefaultPrefaceText =
        "As we discussed recently, our nonprofit has been using Email Money App (EMA) and we've been really pleased with it, and I promised to send you more details.\nPlease read more about it below,\nThanks\nJohn";

    private const string DefaultEmailBodyText =
        "Why we think Email Money App is worth a look:\n\n• EMA automatically records Interac e-Transfers and Pre-Authorized Debits (PADs) straight into your donation database.\n• $1.00 per e-Transfer or PAD, which is far lower than typical credit-card fees (2.2–2.9% + 30¢) or CanadaHelps at 4% or more.\n• One-month sandbox trial with absolutely no contact information required (no email, no phone)\n• Easy to start — import your own donor data or have EMA assist with the conversion for a small one-time fee\n• Built specifically for Canadian churches and non-profits\n\nIf your organization becomes an EMA customer, both of our organizations will receive a one‑month EMA credit, based on the smaller organization's monthly EMA fee.\n\nPS – EMA will not use the name or email gathered for this message to market directly to you.";
}
