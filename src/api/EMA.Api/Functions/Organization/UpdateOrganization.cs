using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using EMA.Api.Services.EmailAliases;
using EMA_Api.Services.EmailAliases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Net;
using System.Text.Json;

namespace EMA_Api.Functions.Organization;

public class UpdateOrganization
{
    private readonly ILogger<UpdateOrganization> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly IEtransferEmailAliasService _aliasService;

    public UpdateOrganization(
        ILogger<UpdateOrganization> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        IEtransferEmailAliasService aliasService)
    {
        _logger = logger;
        _twoFactorCodeStore = twoFactorCodeStore;
        _aliasService = aliasService;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("UpdateOrganization")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "put",
            Route = "organization/{orgID:int}")]
        HttpRequestData req,
        int orgID)
    {
        try
        {
            var actorUserID =
                req.GetRequiredApiIdentity().UserID;

            var organization =
                await JsonSerializer
                    .DeserializeAsync<OrganizationDTO>(
                        req.Body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        },
                        req.FunctionContext.CancellationToken);

            if (organization is null)
            {
                return await CreateMessageResponseAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "Body is required.");
            }

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            var existing =
                await GetExistingVerificationStateAsync(
                    connection,
                    orgID,
                    req.FunctionContext.CancellationToken);

            if (existing is null)
            {
                return await CreateMessageResponseAsync(
                    req,
                    HttpStatusCode.NotFound,
                    "Organization not found.");
            }

            var adminEmailVerified =
                await ResolveEmailVerificationAsync(
                    organization.AdminEmail,
                    organization.AdminEmailVerificationToken,
                    existing.AdminEmail,
                    existing.AdminEmailVerified,
                    TwoFactorPurpose.OrganizationAdminEmail,
                    orgID,
                    "admin email",
                    req.FunctionContext.CancellationToken);

            var organizationEmailVerified =
                await ResolveEmailVerificationAsync(
                    organization.OrganizationEmail,
                    organization.OrganizationEmailVerificationToken,
                    existing.OrganizationEmail,
                    existing.OrganizationEmailVerified,
                    TwoFactorPurpose.OrganizationEmail,
                    orgID,
                    "organization email",
                    req.FunctionContext.CancellationToken);

            var contactEmailVerified =
                await ResolveEmailVerificationAsync(
                    organization.ContactEmail,
                    organization.ContactEmailVerificationToken,
                    existing.ContactEmail,
                    existing.ContactEmailVerified,
                    TwoFactorPurpose.OrganizationContactEmail,
                    orgID,
                    "contact email",
                    req.FunctionContext.CancellationToken);

            var requestedEtransferEmail =
                NormalizeRequestedEtransferEmail(
                    organization.EtransferEmailAddress,
                    existing.EtransferEmailAddress);

            var existingEtransferEmail =
                NormalizeOptional(
                    existing.EtransferEmailAddress);

            var etransferEmailChanged =
                !string.Equals(
                    requestedEtransferEmail,
                    existingEtransferEmail,
                    StringComparison.OrdinalIgnoreCase);

            // Task 22 requires the eTransfer Method of Giving and Fund to be
            // configured before an organization can set its Donation Email.
            // Enforce this on the API as well as in the onboarding UI.
            if (etransferEmailChanged &&
                !string.IsNullOrWhiteSpace(
                    requestedEtransferEmail) &&
                !await HasEtransferSetupPrerequisitesAsync(
                    connection,
                    orgID,
                    req.FunctionContext.CancellationToken))
            {
                return await CreateMessageResponseAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "Set the Method of Giving and Fund for eTransfers before setting the donation email.");
            }

            var etransferEmailVerified =
                etransferEmailChanged
                    ? string.IsNullOrWhiteSpace(
                        requestedEtransferEmail)
                        ? 'N'
                        : 'Y'
                    : existing.EtransferEmailVerified;

            var adminPhoneVerified =
                string.Equals(
                    NormalizeOptional(
                        organization.AdminPhone),
                    NormalizeOptional(
                        existing.AdminPhone),
                    StringComparison.OrdinalIgnoreCase)
                    ? existing.AdminPhoneVerified
                    : 'N';

            SqlTransaction? transaction =
                null;

            var newAliasAdded =
                false;

            var oldAliasRemoved =
                false;

            try
            {
                transaction =
                    (SqlTransaction)
                    await connection.BeginTransactionAsync(
                        req.FunctionContext.CancellationToken);

                if (etransferEmailChanged &&
                    !string.IsNullOrWhiteSpace(
                        requestedEtransferEmail))
                {
                    await AcquireAliasLockAsync(
                        connection,
                        transaction,
                        requestedEtransferEmail,
                        req.FunctionContext.CancellationToken);

                    if (await IsAliasUsedByAnotherOrganizationAsync(
                            connection,
                            transaction,
                            orgID,
                            requestedEtransferEmail,
                            req.FunctionContext.CancellationToken) ||
                        await _aliasService.AliasExistsAsync(
                            requestedEtransferEmail,
                            req.FunctionContext.CancellationToken))
                    {
                        throw new EtransferAliasInUseException(
                            requestedEtransferEmail);
                    }

                    await _aliasService.AddAliasAsync(
                        requestedEtransferEmail,
                        req.FunctionContext.CancellationToken);

                    newAliasAdded =
                        true;
                }

                var rows =
                    await UpdateOrganizationRecordAsync(
                        connection,
                        transaction,
                        orgID,
                        organization,
                        requestedEtransferEmail,
                        adminEmailVerified,
                        organizationEmailVerified,
                        contactEmailVerified,
                        etransferEmailVerified,
                        adminPhoneVerified,
                        actorUserID,
                        req.FunctionContext.CancellationToken);

                if (rows == 0)
                {
                    throw new OrganizationNotFoundException();
                }

                if (etransferEmailChanged &&
                    EtransferAliasAddress.IsManagedAddress(
                        existingEtransferEmail))
                {
                    await _aliasService.RemoveAliasIfExistsAsync(
                        existingEtransferEmail!,
                        req.FunctionContext.CancellationToken);

                    oldAliasRemoved =
                        true;
                }

                await transaction.CommitAsync(
                    req.FunctionContext.CancellationToken);

                return req.CreateResponse(
                    HttpStatusCode.NoContent);
            }
            catch
            {
                if (etransferEmailChanged)
                {
                    await RestoreAliasesAfterFailedUpdateAsync(
                        existingEtransferEmail,
                        requestedEtransferEmail,
                        oldAliasRemoved,
                        newAliasAdded,
                        orgID);
                }

                if (transaction is not null)
                {
                    try
                    {
                        await transaction.RollbackAsync(
                            CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogError(
                            rollbackException,
                            "Failed to roll back organization update transaction for OrganizationID {OrganizationID}.",
                            orgID);
                    }
                }

                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }
        catch (OrganizationNotFoundException)
        {
            return await CreateMessageResponseAsync(
                req,
                HttpStatusCode.NotFound,
                "Organization not found.");
        }
        catch (EtransferAliasBusyException exception)
        {
            return await CreateMessageResponseAsync(
                req,
                HttpStatusCode.Conflict,
                exception.Message);
        }
        catch (EtransferAliasInUseException exception)
        {
            return await CreateMessageResponseAsync(
                req,
                HttpStatusCode.Conflict,
                exception.Message);
        }
        catch (SqlException exception)
            when (exception.Number is 2601 or 2627)
        {
            return await CreateMessageResponseAsync(
                req,
                HttpStatusCode.Conflict,
                "That donation email is already in use. Please choose another email prefix.");
        }
        catch (ArgumentException exception)
        {
            return await CreateMessageResponseAsync(
                req,
                HttpStatusCode.BadRequest,
                exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error updating organization {OrgID}.",
                orgID);

            return await CreateMessageResponseAsync(
                req,
                HttpStatusCode.InternalServerError,
                "Error updating organization.");
        }
    }

    private async Task<int> UpdateOrganizationRecordAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int orgID,
        OrganizationDTO organization,
        string? etransferEmailAddress,
        char adminEmailVerified,
        char organizationEmailVerified,
        char contactEmailVerified,
        char etransferEmailVerified,
        char adminPhoneVerified,
        int actorUserID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.Organization
            SET
                AdminName                  = @AdminName,
                AdminEmail                 = @AdminEmail,
                AdminEmailVerified         = @AdminEmailVerified,
                AdminPhone                 = @AdminPhone,
                AdminPhoneVerified         = @AdminPhoneVerified,
                ContactName                = @ContactName,
                ContactEmail               = @ContactEmail,
                ContactEmailVerified       = @ContactEmailVerified,
                ContactPhone               = @ContactPhone,
                OrganizationName           = @OrganizationName,
                OrganizationPhone          = @OrganizationPhone,
                OrganizationEmail          = @OrganizationEmail,
                OrganizationEmailVerified  = @OrganizationEmailVerified,
                OrganizationWebsite        = @OrganizationWebsite,
                LogoBlobID                 = @LogoBlobID,
                Address                    = @Address,
                City                       = @City,
                ProvinceStateCode          = @ProvinceStateCode,
                PostalCode                 = @PostalCode,
                EtransferEmailAddress      = @EtransferEmailAddress,
                EtransferEmailVerified     = @EtransferEmailVerified,
                CRARegisteredCharityNumber = @CRARegisteredCharityNumber,
                ChMSID                     = @ChMSID,
                AccountingSoftwareID       = @AccountingSoftwareID,
                DateFormatID               = @DateFormatID,
                BankBranchNumber              = @BankBranchNumber,
                BankAccountNumber             = @BankAccountNumber,
                EMAMethodOfGivingIDForCheques = @EMAMethodOfGivingIDForCheques,
                EMAMethodOfGivingIDForCash    = @EMAMethodOfGivingIDForCash,
                CountingSteps                 = @CountingSteps,
                AnnualBudget               = @AnnualBudget,
                OrganizationStatusID       = @OrganizationStatusID,
                ETLDonorDataBlobID         = @ETLDonorDataBlobID,
                ETLDonationDataBlobID      = @ETLDonationDataBlobID,
                DateTimeUpdated            = @DateTimeUpdated,
                UpdatedByUserID            = @UpdatedByUserID
            WHERE EMAOrganizationID = @EMAOrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
            .Value =
                orgID;

        command.Parameters
            .Add(
                "@AdminName",
                SqlDbType.NVarChar,
                255)
            .Value =
                NullableString(
                    organization.AdminName);

        command.Parameters
            .Add(
                "@AdminEmail",
                SqlDbType.NVarChar,
                255)
            .Value =
                NullableString(
                    organization.AdminEmail);

        command.Parameters
            .Add(
                "@AdminEmailVerified",
                SqlDbType.Char,
                1)
            .Value =
                adminEmailVerified.ToString();

        command.Parameters
            .Add(
                "@AdminPhone",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.AdminPhone);

        command.Parameters
            .Add(
                "@AdminPhoneVerified",
                SqlDbType.Char,
                1)
            .Value =
                adminPhoneVerified.ToString();

        command.Parameters
            .Add(
                "@ContactName",
                SqlDbType.NVarChar,
                255)
            .Value =
                NullableString(
                    organization.ContactName);

        command.Parameters
            .Add(
                "@ContactEmail",
                SqlDbType.NVarChar,
                255)
            .Value =
                NullableString(
                    organization.ContactEmail);

        command.Parameters
            .Add(
                "@ContactEmailVerified",
                SqlDbType.Char,
                1)
            .Value =
                contactEmailVerified.ToString();

        command.Parameters
            .Add(
                "@ContactPhone",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.ContactPhone);

        command.Parameters
            .Add(
                "@OrganizationName",
                SqlDbType.VarChar,
                255)
            .Value =
                RequiredString(
                    organization.OrganizationName);

        command.Parameters
            .Add(
                "@OrganizationPhone",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.OrganizationPhone);

        command.Parameters
            .Add(
                "@OrganizationEmail",
                SqlDbType.NVarChar,
                255)
            .Value =
                NullableString(
                    organization.OrganizationEmail);

        command.Parameters
            .Add(
                "@OrganizationEmailVerified",
                SqlDbType.Char,
                1)
            .Value =
                organizationEmailVerified.ToString();

        command.Parameters
            .Add(
                "@OrganizationWebsite",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.OrganizationWebsite);

        command.Parameters
            .Add(
                "@LogoBlobID",
                SqlDbType.VarChar,
                32)
            .Value =
                NullableString(
                    organization.LogoBlobID);

        command.Parameters
            .Add(
                "@Address",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.Address);

        command.Parameters
            .Add(
                "@City",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.City);

        command.Parameters
            .Add(
                "@ProvinceStateCode",
                SqlDbType.Char,
                2)
            .Value =
                NullableString(
                    organization.ProvinceStateCode);

        command.Parameters
            .Add(
                "@PostalCode",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.PostalCode);

        command.Parameters
            .Add(
                "@EtransferEmailAddress",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    etransferEmailAddress);

        command.Parameters
            .Add(
                "@EtransferEmailVerified",
                SqlDbType.Char,
                1)
            .Value =
                etransferEmailVerified.ToString();

        command.Parameters
            .Add(
                "@CRARegisteredCharityNumber",
                SqlDbType.NVarChar,
                255)
            .Value =
                NullableString(
                    organization.CRARegisteredCharityNumber);

        command.Parameters
            .Add(
                "@ChMSID",
                SqlDbType.Int)
            .Value =
                NullableInt(
                    organization.ChMSID);

        command.Parameters
            .Add(
                "@AccountingSoftwareID",
                SqlDbType.Int)
            .Value =
                NullableInt(
                    organization.AccountingSoftwareID);

        command.Parameters
            .Add(
                "@DateFormatID",
                SqlDbType.Char,
                12)
            .Value =
                NullableString(
                    organization.DateFormatID);

        command.Parameters
            .Add(
                "@BankBranchNumber",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.BankBranchNumber);

        command.Parameters
            .Add(
                "@BankAccountNumber",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.BankAccountNumber);

        command.Parameters
            .Add(
                "@EMAMethodOfGivingIDForCheques",
                SqlDbType.Int)
            .Value =
                NullableInt(
                    organization.EMAMethodOfGivingIDForCheques);

        command.Parameters
            .Add(
                "@EMAMethodOfGivingIDForCash",
                SqlDbType.Int)
            .Value =
                NullableInt(
                    organization.EMAMethodOfGivingIDForCash);

        command.Parameters
            .Add(
                "@CountingSteps",
                SqlDbType.NVarChar,
                -1)
            .Value =
                NullableString(
                    organization.CountingSteps);

        command.Parameters
            .Add(
                "@AnnualBudget",
                SqlDbType.VarChar,
                255)
            .Value =
                NullableString(
                    organization.AnnualBudget);

        command.Parameters
            .Add(
                "@OrganizationStatusID",
                SqlDbType.Int)
            .Value =
                NullableInt(
                    organization.OrganizationStatusID);

        command.Parameters
            .Add(
                "@ETLDonorDataBlobID",
                SqlDbType.VarChar,
                36)
            .Value =
                NullableString(
                    organization.ETLDonorDataBlobID);

        command.Parameters
            .Add(
                "@ETLDonationDataBlobID",
                SqlDbType.VarChar,
                36)
            .Value =
                NullableString(
                    organization.ETLDonationDataBlobID);

        command.Parameters
            .Add(
                "@DateTimeUpdated",
                SqlDbType.DateTime2)
            .Value =
                DateTime.UtcNow;

        command.Parameters
            .Add(
                "@UpdatedByUserID",
                SqlDbType.Int)
            .Value =
                actorUserID;

        return await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private async Task RestoreAliasesAfterFailedUpdateAsync(
        string? existingEmailAddress,
        string? requestedEmailAddress,
        bool oldAliasRemoved,
        bool newAliasAdded,
        int organizationID)
    {
        try
        {
            if (oldAliasRemoved &&
                EtransferAliasAddress.IsManagedAddress(
                    existingEmailAddress))
            {
                await _aliasService.EnsureAliasAsync(
                    existingEmailAddress!,
                    CancellationToken.None);
            }

            if (newAliasAdded &&
                EtransferAliasAddress.IsManagedAddress(
                    requestedEmailAddress))
            {
                await _aliasService.RemoveAliasIfExistsAsync(
                    requestedEmailAddress!,
                    CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "Failed to restore Exchange Online aliases after an organization update failure. OrganizationID={OrganizationID}, ExistingAlias={ExistingAlias}, RequestedAlias={RequestedAlias}.",
                organizationID,
                existingEmailAddress,
                requestedEmailAddress);
        }
    }

    private async Task<char> ResolveEmailVerificationAsync(
        string? requestedEmail,
        string? verificationToken,
        string? existingEmail,
        char existingVerified,
        TwoFactorPurpose purpose,
        int EMAOrganizationID,
        string fieldName,
        CancellationToken cancellationToken)
    {
        var normalizedRequestedEmail =
            NormalizeOptionalEmail(
                requestedEmail);

        var normalizedExistingEmail =
            NormalizeOptionalEmail(
                existingEmail);

        if (string.Equals(
                normalizedRequestedEmail,
                normalizedExistingEmail,
                StringComparison.Ordinal))
        {
            if (existingVerified == 'Y')
            {
                return 'Y';
            }

            if (string.IsNullOrWhiteSpace(
                    verificationToken))
            {
                return 'N';
            }
        }
        else if (string.IsNullOrWhiteSpace(
                     normalizedRequestedEmail))
        {
            return 'N';
        }
        else if (string.IsNullOrWhiteSpace(
                     verificationToken))
        {
            throw new ArgumentException(
                $"The {fieldName} must be verified before it can be changed.");
        }

        if (string.IsNullOrWhiteSpace(
                normalizedRequestedEmail) ||
            string.IsNullOrWhiteSpace(
                verificationToken))
        {
            return 'N';
        }

        var verified =
            await _twoFactorCodeStore
                .ConsumeVerifiedCodeAsync(
                    verificationToken,
                    purpose,
                    EMAOrganizationID,
                    normalizedRequestedEmail,
                    cancellationToken);

        if (!verified)
        {
            throw new ArgumentException(
                $"The {fieldName} verification is invalid or expired. Please verify it again.");
        }

        return 'Y';
    }

    private static async Task<ExistingVerificationState?>
        GetExistingVerificationStateAsync(
            SqlConnection connection,
            int EMAOrganizationID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                AdminEmail,
                AdminEmailVerified,
                AdminPhone,
                AdminPhoneVerified,
                OrganizationEmail,
                OrganizationEmailVerified,
                ContactEmail,
                ContactEmailVerified,
                EtransferEmailAddress,
                EtransferEmailVerified
            FROM dbo.Organization
            WHERE EMAOrganizationID = @EMAOrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
            .Value =
                EMAOrganizationID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new ExistingVerificationState
        {
            AdminEmail =
                GetNullableString(
                    reader,
                    "AdminEmail"),

            AdminEmailVerified =
                GetVerificationFlag(
                    reader,
                    "AdminEmailVerified"),

            AdminPhone =
                GetNullableString(
                    reader,
                    "AdminPhone"),

            AdminPhoneVerified =
                GetVerificationFlag(
                    reader,
                    "AdminPhoneVerified"),

            OrganizationEmail =
                GetNullableString(
                    reader,
                    "OrganizationEmail"),

            OrganizationEmailVerified =
                GetVerificationFlag(
                    reader,
                    "OrganizationEmailVerified"),

            ContactEmail =
                GetNullableString(
                    reader,
                    "ContactEmail"),

            ContactEmailVerified =
                GetVerificationFlag(
                    reader,
                    "ContactEmailVerified"),

            EtransferEmailAddress =
                GetNullableString(
                    reader,
                    "EtransferEmailAddress"),

            EtransferEmailVerified =
                GetVerificationFlag(
                    reader,
                    "EtransferEmailVerified")
        };
    }

    private static async Task AcquireAliasLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string emailAddress,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @LockResult int;

            EXEC @LockResult = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;

            SELECT @LockResult;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@Resource",
                SqlDbType.NVarChar,
                255)
            .Value =
                $"EMA:EtransferAlias:{emailAddress}";

        var result =
            Convert.ToInt32(
                await command.ExecuteScalarAsync(
                    cancellationToken));

        if (result < 0)
        {
            throw new EtransferAliasBusyException(
                emailAddress);
        }
    }

    private static async Task<bool> HasEtransferSetupPrerequisitesAsync(
        SqlConnection connection,
        int organizationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE
                WHEN EMAMethodOfGivingIDForETransfers IS NOT NULL
                 AND EMAFundIDForEtransfer IS NOT NULL
                    THEN 1
                ELSE 0
            END
            FROM dbo.Organization
            WHERE EMAOrganizationID = @EMAOrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)
                   ?? 0) == 1;
    }

    private static async Task<bool>
        IsAliasUsedByAnotherOrganizationAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int organizationID,
            string emailAddress,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.Organization
            WHERE EMAOrganizationID <> @EMAOrganizationID
              AND LOWER(LTRIM(RTRIM(EtransferEmailAddress))) = @EtransferEmailAddress;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
            .Value =
                organizationID;

        command.Parameters
            .Add(
                "@EtransferEmailAddress",
                SqlDbType.VarChar,
                255)
            .Value =
                emailAddress;

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is not null;
    }

    private static string? NormalizeRequestedEtransferEmail(
        string? requestedEmailAddress,
        string? existingEmailAddress)
    {
        if (string.IsNullOrWhiteSpace(
                requestedEmailAddress))
        {
            if (string.IsNullOrWhiteSpace(
                    existingEmailAddress))
            {
                return null;
            }

            throw new ArgumentException(
                "The donation email address is required.");
        }

        return EtransferAliasAddress.NormalizeEmailAddress(
            requestedEmailAddress);
    }

    private static string? NormalizeOptionalEmail(
        string? emailAddress) =>
        string.IsNullOrWhiteSpace(emailAddress)
            ? null
            : EmailAddressNormalizer.Normalize(
                emailAddress);

    private static string? NormalizeOptional(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static string? GetNullableString(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static char GetVerificationFlag(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(ordinal))
        {
            return 'N';
        }

        var value =
            reader
                .GetValue(ordinal)
                ?.ToString();

        return string.Equals(
            value,
            "Y",
            StringComparison.OrdinalIgnoreCase)
                ? 'Y'
                : 'N';
    }

    private static object RequiredString(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "OrganizationName is required.")
            : value.Trim();

    private static object NullableString(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();

    private static object NullableInt(
        int? value) =>
        value.HasValue
            ? value.Value
            : DBNull.Value;

    private static async Task<HttpResponseData>
        CreateMessageResponseAsync(
            HttpRequestData req,
            HttpStatusCode statusCode,
            string message)
    {
        var response =
            req.CreateResponse(
                statusCode);

        await response.WriteAsJsonAsync(new
        {
            message
        });

        return response;
    }

    private sealed class ExistingVerificationState
    {
        public string? AdminEmail { get; init; }
        public char AdminEmailVerified { get; init; }
        public string? AdminPhone { get; init; }
        public char AdminPhoneVerified { get; init; }
        public string? OrganizationEmail { get; init; }
        public char OrganizationEmailVerified { get; init; }
        public string? ContactEmail { get; init; }
        public char ContactEmailVerified { get; init; }
        public string? EtransferEmailAddress { get; init; }
        public char EtransferEmailVerified { get; init; }
    }

    private sealed class OrganizationNotFoundException : Exception
    {
    }
}
