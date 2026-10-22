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

namespace EMA_Api.Functions.Organization;

public class CreateOrganization
{
    private readonly ILogger<CreateOrganization> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly IEtransferEmailAliasService _aliasService;

    public CreateOrganization(
        ILogger<CreateOrganization> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        IEtransferEmailAliasService aliasService)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _aliasService =
            aliasService;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("CreateOrganization")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "organization")]
        HttpRequestData req)
    {
        try
        {
            var actorUserID =
                req.GetRequiredApiIdentity().UserID;

            var org =
                await req.ReadFromJsonAsync<OrganizationDTO>();

            if (org is null)
            {
                var bad =
                    req.CreateResponse(
                        HttpStatusCode.BadRequest);

                await bad.WriteAsJsonAsync(new
                {
                    message =
                        "Body is required."
                });

                return bad;
            }

            var logoBlobID =
                CreateOrNormalizeBlobID(
                    org.LogoBlobID);

            var adminEmailVerified =
                await RequireVerifiedEmailAsync(
                    org.AdminEmail,
                    org.AdminEmailVerificationToken,
                    TwoFactorPurpose
                        .NewOrganizationAdminEmail,
                    "admin email",
                    req.FunctionContext.CancellationToken);

            var etransferEmailAddress =
                string.IsNullOrWhiteSpace(
                    org.EtransferEmailAddress)
                    ? null
                    : EtransferAliasAddress.NormalizeEmailAddress(
                        org.EtransferEmailAddress);

            var etransferEmailVerified =
                string.IsNullOrWhiteSpace(
                    etransferEmailAddress)
                    ? 'N'
                    : 'Y';

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            SqlTransaction? transaction =
                null;

            var aliasAdded =
                false;

            try
            {
                transaction =
                    (SqlTransaction)
                    await connection.BeginTransactionAsync(
                        req.FunctionContext.CancellationToken);

                if (!string.IsNullOrWhiteSpace(
                        etransferEmailAddress))
                {
                    await AcquireAliasLockAsync(
                        connection,
                        transaction,
                        etransferEmailAddress,
                        req.FunctionContext.CancellationToken);

                    if (await IsAliasUsedByAnotherOrganizationAsync(
                            connection,
                            transaction,
                            etransferEmailAddress,
                            req.FunctionContext.CancellationToken) ||
                        await _aliasService.AliasExistsAsync(
                            etransferEmailAddress,
                            req.FunctionContext.CancellationToken))
                    {
                        throw new EtransferAliasInUseException(
                            etransferEmailAddress);
                    }

                    await _aliasService.AddAliasAsync(
                        etransferEmailAddress,
                        req.FunctionContext.CancellationToken);

                    aliasAdded =
                        true;
                }

                const string sql = """
                    INSERT INTO dbo.Organization
                    (
                        AdminName,
                        AdminEmail,
                        AdminEmailVerified,
                        AdminPhone,
                        AdminPhoneVerified,
                        ContactName,
                        ContactEmail,
                        ContactPhone,
                        OrganizationName,
                        OrganizationPhone,
                        OrganizationEmail,
                        OrganizationWebsite,
                        Address,
                        City,
                        ProvinceStateCode,
                        PostalCode,
                        EtransferEmailAddress,
                        EtransferEmailVerified,
                        CRARegisteredCharityNumber,
                        ChMSID,
                        AccountingSoftwareID,
                        AnnualBudget,
                        OrganizationStatusID,
                        LogoBlobID,
                        DateTimeCreated,
                        CreatedByUserID,
                        DateTimeUpdated,
                        UpdatedByUserID
                    )
                    VALUES
                    (
                        @AdminName,
                        @AdminEmail,
                        @AdminEmailVerified,
                        @AdminPhone,
                        @AdminPhoneVerified,
                        @ContactName,
                        @ContactEmail,
                        @ContactPhone,
                        @OrganizationName,
                        @OrganizationPhone,
                        @OrganizationEmail,
                        @OrganizationWebsite,
                        @Address,
                        @City,
                        @ProvinceStateCode,
                        @PostalCode,
                        @EtransferEmailAddress,
                        @EtransferEmailVerified,
                        @CRARegisteredCharityNumber,
                        @ChMSID,
                        @AccountingSoftwareID,
                        @AnnualBudget,
                        @OrganizationStatusID,
                        @LogoBlobID,
                        @DateTimeCreated,
                        @CreatedByUserID,
                        @DateTimeUpdated,
                        @UpdatedByUserID
                    );
                    """;

                await using var command =
                    new SqlCommand(
                        sql,
                        connection,
                        transaction);

                command.Parameters
                    .Add(
                        "@AdminName",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.AdminName);

                command.Parameters
                    .Add(
                        "@AdminEmail",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.AdminEmail);

                /*
                 * Never use org.AdminEmailVerified here.
                 * Only the server-generated proof controls this value.
                 */
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
                            org.AdminPhone);

                /*
                 * Phone verification is not implemented in this flow.
                 * Do not trust a browser-supplied verified flag.
                 */
                command.Parameters
                    .Add(
                        "@AdminPhoneVerified",
                        SqlDbType.Char,
                        1)
                    .Value =
                        "N";

                command.Parameters
                    .Add(
                        "@ContactName",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.ContactName);

                command.Parameters
                    .Add(
                        "@ContactEmail",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.ContactEmail);

                command.Parameters
                    .Add(
                        "@ContactPhone",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.ContactPhone);

                command.Parameters
                    .Add(
                        "@OrganizationName",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        RequiredString(
                            org.OrganizationName);

                command.Parameters
                    .Add(
                        "@OrganizationPhone",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.OrganizationPhone);

                command.Parameters
                    .Add(
                        "@OrganizationEmail",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.OrganizationEmail);

                command.Parameters
                    .Add(
                        "@OrganizationWebsite",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.OrganizationWebsite);

                command.Parameters
                    .Add(
                        "@Address",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.Address);

                command.Parameters
                    .Add(
                        "@City",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.City);

                command.Parameters
                    .Add(
                        "@ProvinceStateCode",
                        SqlDbType.Char,
                        2)
                    .Value =
                        NullableString(
                            org.ProvinceStateCode);

                command.Parameters
                    .Add(
                        "@PostalCode",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.PostalCode);

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
                        510)
                    .Value =
                        NullableString(
                            org.CRARegisteredCharityNumber);

                command.Parameters
                    .Add(
                        "@ChMSID",
                        SqlDbType.Int)
                    .Value =
                        NullableInt(
                            org.ChMSID);

                command.Parameters
                    .Add(
                        "@AccountingSoftwareID",
                        SqlDbType.Int)
                    .Value =
                        NullableInt(
                            org.AccountingSoftwareID);

                command.Parameters
                    .Add(
                        "@AnnualBudget",
                        SqlDbType.VarChar,
                        255)
                    .Value =
                        NullableString(
                            org.AnnualBudget);

                command.Parameters
                    .Add(
                        "@OrganizationStatusID",
                        SqlDbType.Int)
                    .Value =
                        NullableInt(
                            org.OrganizationStatusID);

                command.Parameters
                    .Add(
                        "@LogoBlobID",
                        SqlDbType.VarChar,
                        32)
                    .Value =
                        logoBlobID;

                command.Parameters
                    .Add(
                        "@DateTimeCreated",
                        SqlDbType.DateTime2)
                    .Value =
                        DateTime.UtcNow;

                command.Parameters
                    .Add(
                        "@CreatedByUserID",
                        SqlDbType.Int)
                    .Value =
                        actorUserID;

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

                await command.ExecuteNonQueryAsync(
                    req.FunctionContext.CancellationToken);

                await transaction.CommitAsync(
                    req.FunctionContext.CancellationToken);

                return req.CreateResponse(
                    HttpStatusCode.Created);
            }
            catch
            {
                if (aliasAdded &&
                    !string.IsNullOrWhiteSpace(
                        etransferEmailAddress))
                {
                    try
                    {
                        await _aliasService.RemoveAliasIfExistsAsync(
                            etransferEmailAddress,
                            CancellationToken.None);
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogCritical(
                            cleanupException,
                            "Failed to remove alias {EmailAddress} after organization creation failed.",
                            etransferEmailAddress);
                    }
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
                            "Failed to roll back organization creation transaction.");
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
        catch (EtransferAliasBusyException exception)
        {
            var conflict =
                req.CreateResponse(
                    HttpStatusCode.Conflict);

            await conflict.WriteAsJsonAsync(new
            {
                message = exception.Message
            });

            return conflict;
        }
        catch (EtransferAliasInUseException exception)
        {
            var conflict =
                req.CreateResponse(
                    HttpStatusCode.Conflict);

            await conflict.WriteAsJsonAsync(new
            {
                message = exception.Message
            });

            return conflict;
        }
        catch (SqlException exception)
            when (exception.Number is 2601 or 2627)
        {
            var conflict =
                req.CreateResponse(
                    HttpStatusCode.Conflict);

            await conflict.WriteAsJsonAsync(new
            {
                message =
                    "That donation email is already in use. Please choose another email prefix."
            });

            return conflict;
        }
        catch (ArgumentException exception)
        {
            var bad =
                req.CreateResponse(
                    HttpStatusCode.BadRequest);

            await bad.WriteAsJsonAsync(new
            {
                message =
                    exception.Message
            });

            return bad;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error creating organization.");

            var error =
                req.CreateResponse(
                    HttpStatusCode.InternalServerError);

            await error.WriteAsJsonAsync(new
            {
                message =
                    "Error creating organization."
            });

            return error;
        }
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

    private static async Task<bool>
        IsAliasUsedByAnotherOrganizationAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string emailAddress,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.Organization
            WHERE LOWER(LTRIM(RTRIM(EtransferEmailAddress))) = @EtransferEmailAddress;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

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

    private static string CreateOrNormalizeBlobID(
        string? existingBlobID)
    {
        if (!string.IsNullOrWhiteSpace(
                existingBlobID) &&
            existingBlobID.Length == 32 &&
            Guid.TryParseExact(
                existingBlobID,
                "N",
                out _))
        {
            return existingBlobID
                .Trim()
                .ToLowerInvariant();
        }

        return Guid
            .NewGuid()
            .ToString("N");
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
        value.HasValue &&
        value.Value > 0
            ? value.Value
            : DBNull.Value;

    private async Task<char> RequireVerifiedEmailAsync(
        string? emailAddress,
        string? verificationToken,
        TwoFactorPurpose purpose,
        string fieldName,
        CancellationToken cancellationToken,
        bool requireWhenPresent = true)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            return 'N';
        }

        if (!requireWhenPresent &&
            string.IsNullOrWhiteSpace(
                verificationToken))
        {
            return 'N';
        }

        if (string.IsNullOrWhiteSpace(
                verificationToken))
        {
            throw new ArgumentException(
                $"The {fieldName} must be verified before the organization can be created.");
        }

        var verified =
            await _twoFactorCodeStore
                .ConsumeVerifiedCodeAsync(
                    verificationToken,
                    purpose,
                    actorID:
                        0,
                    emailAddress:
                        emailAddress,
                    cancellationToken:
                        cancellationToken);

        if (!verified)
        {
            throw new ArgumentException(
                $"The {fieldName} verification is invalid or expired. Please verify it again.");
        }

        return 'Y';
    }
}
