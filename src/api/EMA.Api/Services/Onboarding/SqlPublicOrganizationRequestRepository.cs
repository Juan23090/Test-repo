using System.Data;
using EMA.Api.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Onboarding;

public sealed class SqlPublicOrganizationRequestRepository : IPublicOrganizationRequestRepository
{
    private readonly string _connectionString;

    public SqlPublicOrganizationRequestRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    public async Task<PublicOnboardingLookupsDto> GetLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        const string chmsSql = """
            SELECT ChMSID, ChMSName
            FROM dbo.ChMS
            ORDER BY ChMSName;
            """;

        const string accountingSql = """
            SELECT AccountingSoftwareID, [Name]
            FROM dbo.AccountingSoftware
            ORDER BY [Name];
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var result = new PublicOnboardingLookupsDto();

        await using (var command = new SqlCommand(chmsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.ChMSOptions.Add(new PublicChMSOptionDto
                {
                    ChMSID = reader.GetInt32(0),
                    ChMSName = reader.GetString(1)
                });
            }
        }

        await using (var command = new SqlCommand(accountingSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.AccountingSoftwareOptions.Add(new PublicAccountingSoftwareOptionDto
                {
                    AccountingSoftwareID = reader.GetInt32(0),
                    Name = reader.GetString(1)
                });
            }
        }

        return result;
    }

    public async Task<int> CreateAsync(
        PublicOrganizationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!request.OrganizationStatusID.HasValue)
        {
            throw new ArgumentException("An onboarding option is required.");
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ValidateReferenceDataAsync(connection, transaction, request, cancellationToken);
            var systemUserID = await GetFollowupUserIDAsync(connection, transaction, cancellationToken);

            // TR_Organization_AfterInsert synchronously creates/reuses the admin User,
            // assigns Role 10, and creates the initial OrganizationJourney record.
            // Do not duplicate those trigger-owned writes here.
            var organizationID = await InsertOrganizationAsync(
                connection,
                transaction,
                request,
                systemUserID,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return organizationID;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ValidateReferenceDataAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PublicOrganizationRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!PublicOrganizationRequestValidator.IsAllowedStatus(request.OrganizationStatusID))
        {
            throw new ArgumentException("Please choose a valid onboarding option.");
        }

        const string sql = """
            SELECT
                CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.ChMS
                    WHERE ChMSID = @ChMSID
                ) THEN 1 ELSE 0 END,
                CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.AccountingSoftware
                    WHERE AccountingSoftwareID = @AccountingSoftwareID
                ) THEN 1 ELSE 0 END,
                CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.OrganizationStatus
                    WHERE OrganizationStatusID = @OrganizationStatusID
                ) THEN 1 ELSE 0 END;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@ChMSID", SqlDbType.Int).Value = request.ChMSID!.Value;
        command.Parameters.Add("@AccountingSoftwareID", SqlDbType.Int).Value = request.AccountingSoftwareID!.Value;
        command.Parameters.Add("@OrganizationStatusID", SqlDbType.SmallInt).Value = request.OrganizationStatusID!.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Unable to validate onboarding reference data.");
        }

        if (reader.GetInt32(0) != 1)
        {
            throw new ArgumentException("Please choose a valid ChMS.");
        }

        if (reader.GetInt32(1) != 1)
        {
            throw new ArgumentException("Please choose valid accounting software.");
        }

        if (reader.GetInt32(2) != 1)
        {
            throw new ArgumentException("Please choose a valid onboarding option.");
        }
    }

    private static async Task<int> GetFollowupUserIDAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) UserIDForFollowups
            FROM dbo.SystemSettings
            WHERE UserIDForFollowups IS NOT NULL
            ORDER BY SettingID DESC;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            throw new InvalidOperationException("SystemSettings.UserIDForFollowups is not configured.");
        }

        return Convert.ToInt32(value);
    }

    private static async Task<int> InsertOrganizationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PublicOrganizationRequestDto request,
        int systemUserID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @InsertedOrganization TABLE
            (
                EMAOrganizationID INT NOT NULL
            );

            INSERT INTO dbo.Organization
            (
                OrganizationName,
                AdminName,
                AdminEmail,
                AdminEmailVerified,
                AdminPhone,
                AdminPhoneVerified,
                ContactName,
                ContactEmail,
                ContactEmailVerified,
                ContactPhone,
                OrganizationEmailVerified,
                EtransferEmailVerified,
                Address,
                City,
                ProvinceStateCode,
                PostalCode,
                AnnualBudget,
                ChMSID,
                AccountingSoftwareID,
                OrganizationStatusID,
                DateTimeCreated,
                CreatedByUserID,
                DateTimeUpdated,
                UpdatedByUserID
            )
            OUTPUT INSERTED.EMAOrganizationID
                INTO @InsertedOrganization (EMAOrganizationID)
            VALUES
            (
                @OrganizationName,
                @AdminName,
                @AdminEmail,
                'Y',
                @AdminPhone,
                'N',
                @AdminName,
                @AdminEmail,
                'Y',
                @AdminPhone,
                'N',
                'N',
                @Address,
                @City,
                @ProvinceStateCode,
                @PostalCode,
                @AnnualBudget,
                @ChMSID,
                @AccountingSoftwareID,
                @OrganizationStatusID,
                SYSUTCDATETIME(),
                @SystemUserID,
                SYSUTCDATETIME(),
                @SystemUserID
            );

            SELECT TOP (1) EMAOrganizationID
            FROM @InsertedOrganization;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        AddRequiredString(command, "@OrganizationName", request.OrganizationName, 255);
        AddRequiredString(command, "@AdminName", request.AdminName, 255);
        AddRequiredString(command, "@AdminEmail", request.AdminEmail, 255);
        AddRequiredString(command, "@AdminPhone", request.AdminPhone, 255);
        AddRequiredString(command, "@Address", request.Address, 255);
        AddRequiredString(command, "@City", request.City, 255);
        AddRequiredString(command, "@ProvinceStateCode", request.ProvinceStateCode, 2, SqlDbType.Char);
        AddRequiredString(command, "@PostalCode", request.PostalCode, 255);
        AddRequiredString(command, "@AnnualBudget", request.AnnualBudget, 255);
        command.Parameters.Add("@ChMSID", SqlDbType.Int).Value = request.ChMSID!.Value;
        command.Parameters.Add("@AccountingSoftwareID", SqlDbType.Int).Value = request.AccountingSoftwareID!.Value;
        command.Parameters.Add("@OrganizationStatusID", SqlDbType.SmallInt).Value = request.OrganizationStatusID!.Value;
        command.Parameters.Add("@SystemUserID", SqlDbType.Int).Value = systemUserID;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static void AddRequiredString(
        SqlCommand command,
        string name,
        string? value,
        int length,
        SqlDbType type = SqlDbType.VarChar)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name.TrimStart('@')} is required.");
        }

        command.Parameters.Add(name, type, length).Value = value.Trim();
    }
}
