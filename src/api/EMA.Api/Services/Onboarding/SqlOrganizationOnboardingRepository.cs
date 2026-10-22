using System.Data;
using EMA.Api.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Onboarding;

public sealed class SqlOrganizationOnboardingRepository : IOrganizationOnboardingRepository
{
    internal const string CreditDebitMigrationFile =
        "src/db/20260817_Task22DonationAttributeOnboarding.sql";

    private const int MaximumBankValueLength = 255;

    private readonly string _connectionString;

    public SqlOrganizationOnboardingRepository(
        IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public async Task<OrganizationOnboardingSettingsDto?> GetAsync(
        int organizationID,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        var creditDebitSchemaAvailable =
            await HasCreditDebitSchemaAsync(
                connection,
                transaction: null,
                cancellationToken);

        var creditDebitColumns =
            creditDebitSchemaAvailable
                ? """
                  , EMAEventIDForCreditDebitCards
                  , EMASourceIDForCreditDebitCards
                  , EMAMethodOfGivingIDForCreditDebitCards
                  , EMAFundIDForCreditDebitCards
                  , EMACampusIDForCreditDebitCards
                  """
                : string.Empty;

        var sql = $"""
            SELECT
                EMAOrganizationID,
                UseAnonymous,
                UseCampuses,
                UseEvents,
                UseFunds,
                UseMethodsOfGiving,
                UseSources,
                UseTributeTypes,
                EMADonorIDForAnonymous,
                BankBranchNumber,
                BankAccountNumber,
                EMAMethodOfGivingIDForCheques,
                EMAMethodOfGivingIDForCash,
                EMAEventIDForEtransfers,
                EMASourceIDForEtransfers,
                EMAMethodOfGivingIDForETransfers,
                EMAFundIDForEtransfer,
                EMACampusIDForEtransfers,
                EMAEventIDForPADs,
                EMASourceIDForPADs,
                EMAMethodOfGivingIDForPADs,
                EMAFundIDForPADs,
                EMACampusIDForPADs
                {creditDebitColumns}
            FROM dbo.Organization
            WHERE EMAOrganizationID = @OrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        var settings =
            new OrganizationOnboardingSettingsDto
            {
                EMAOrganizationID =
                    organizationID,

                UseAnonymous =
                    GetUseFlag(
                        reader,
                        "UseAnonymous"),

                UseCampuses =
                    GetUseFlag(
                        reader,
                        "UseCampuses"),

                UseEvents =
                    GetUseFlag(
                        reader,
                        "UseEvents"),

                UseFunds =
                    GetUseFlag(
                        reader,
                        "UseFunds"),

                UseMethodsOfGiving =
                    GetUseFlag(
                        reader,
                        "UseMethodsOfGiving"),

                UseSources =
                    GetUseFlag(
                        reader,
                        "UseSources"),

                UseTributeTypes =
                    GetUseFlag(
                        reader,
                        "UseTributeTypes"),

                EMADonorIDForAnonymous =
                    GetNullableInt(
                        reader,
                        "EMADonorIDForAnonymous"),

                BankBranchNumber =
                    GetNullableString(
                        reader,
                        "BankBranchNumber"),

                BankAccountNumber =
                    GetNullableString(
                        reader,
                        "BankAccountNumber"),

                EMAMethodOfGivingIDForCheques =
                    GetNullableInt(
                        reader,
                        "EMAMethodOfGivingIDForCheques"),

                EMAMethodOfGivingIDForCash =
                    GetNullableInt(
                        reader,
                        "EMAMethodOfGivingIDForCash"),

                EMAEventIDForEtransfers =
                    GetNullableInt(
                        reader,
                        "EMAEventIDForEtransfers"),

                EMASourceIDForEtransfers =
                    GetNullableInt(
                        reader,
                        "EMASourceIDForEtransfers"),

                EMAMethodOfGivingIDForETransfers =
                    GetNullableInt(
                        reader,
                        "EMAMethodOfGivingIDForETransfers"),

                EMAFundIDForEtransfer =
                    GetNullableInt(
                        reader,
                        "EMAFundIDForEtransfer"),

                EMACampusIDForEtransfers =
                    GetNullableInt(
                        reader,
                        "EMACampusIDForEtransfers"),

                EMAEventIDForPADs =
                    GetNullableInt(
                        reader,
                        "EMAEventIDForPADs"),

                EMASourceIDForPADs =
                    GetNullableInt(
                        reader,
                        "EMASourceIDForPADs"),

                EMAMethodOfGivingIDForPADs =
                    GetNullableInt(
                        reader,
                        "EMAMethodOfGivingIDForPADs"),

                EMAFundIDForPADs =
                    GetNullableInt(
                        reader,
                        "EMAFundIDForPADs"),

                EMACampusIDForPADs =
                    GetNullableInt(
                        reader,
                        "EMACampusIDForPADs"),

                CreditDebitSchemaAvailable =
                    creditDebitSchemaAvailable
            };

        if (creditDebitSchemaAvailable)
        {
            settings.EMAEventIDForCreditDebitCards =
                GetNullableInt(
                    reader,
                    "EMAEventIDForCreditDebitCards");

            settings.EMASourceIDForCreditDebitCards =
                GetNullableInt(
                    reader,
                    "EMASourceIDForCreditDebitCards");

            settings.EMAMethodOfGivingIDForCreditDebitCards =
                GetNullableInt(
                    reader,
                    "EMAMethodOfGivingIDForCreditDebitCards");

            settings.EMAFundIDForCreditDebitCards =
                GetNullableInt(
                    reader,
                    "EMAFundIDForCreditDebitCards");

            settings.EMACampusIDForCreditDebitCards =
                GetNullableInt(
                    reader,
                    "EMACampusIDForCreditDebitCards");
        }

        return settings;
    }

    public async Task<OrganizationOnboardingSaveResult> SaveAsync(
        int organizationID,
        int actorUserID,
        string section,
        SaveOrganizationOnboardingSettingsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var normalizedSection =
            NormalizeSection(
                section);

        if (normalizedSection is null)
        {
            return OrganizationOnboardingSaveResult.Invalid(
                "Unknown onboarding setup section.");
        }

        var requestValidation =
            ValidateRequestValues(
                normalizedSection,
                request);

        if (!requestValidation.Succeeded)
        {
            return requestValidation;
        }

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var transaction =
            (SqlTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            if (!await OrganizationExistsAsync(
                    connection,
                    transaction,
                    organizationID,
                    cancellationToken))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return OrganizationOnboardingSaveResult.Invalid(
                    "Organization not found.");
            }

            if (normalizedSection == "crdb" &&
                !await HasCreditDebitSchemaAsync(
                    connection,
                    transaction,
                    cancellationToken))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return OrganizationOnboardingSaveResult.SchemaConflict(
                    $"Credit and Debit Card defaults require the approved database update in {CreditDebitMigrationFile}.");
            }

            if (normalizedSection == "etransfers" &&
                !HasEtransferPrerequisites(
                    request) &&
                await HasEtransferEmailAsync(
                    connection,
                    transaction,
                    organizationID,
                    cancellationToken))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return OrganizationOnboardingSaveResult.Invalid(
                    "Method of Giving and Fund are required while a donation email is configured.");
            }

            var referenceValidation =
                await ValidateReferencesAsync(
                    connection,
                    transaction,
                    organizationID,
                    normalizedSection,
                    request,
                    cancellationToken);

            if (!referenceValidation.Succeeded)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return referenceValidation;
            }

            var rows =
                await UpdateSectionAsync(
                    connection,
                    transaction,
                    organizationID,
                    actorUserID,
                    normalizedSection,
                    request,
                    cancellationToken);

            if (rows != 1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return OrganizationOnboardingSaveResult.Invalid(
                    "Organization not found.");
            }

            await transaction.CommitAsync(
                cancellationToken);

            return OrganizationOnboardingSaveResult.Success();
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original exception.
            }

            throw;
        }
    }

    internal static string? NormalizeSection(
        string? section) =>
        section?.Trim().ToLowerInvariant()
            switch
            {
                "anonymous" => "anonymous",
                "deposit" => "deposit",
                "deposit-slip" => "deposit",
                "etransfers" => "etransfers",
                "pads" => "pads",
                "crdb" => "crdb",
                _ => null
            };

    internal static bool HasEtransferPrerequisites(
        SaveOrganizationOnboardingSettingsRequestDto request) =>
        request.EMAMethodOfGivingIDForETransfers is > 0 &&
        request.EMAFundIDForEtransfer is > 0;

    internal static OrganizationOnboardingSaveResult ValidateRequestValues(
        string section,
        SaveOrganizationOnboardingSettingsRequestDto request)
    {
        if (section == "deposit")
        {
            if ((request.BankBranchNumber?.Trim().Length ?? 0) >
                MaximumBankValueLength)
            {
                return OrganizationOnboardingSaveResult.Invalid(
                    $"Branch must be {MaximumBankValueLength} characters or fewer.");
            }

            if ((request.BankAccountNumber?.Trim().Length ?? 0) >
                MaximumBankValueLength)
            {
                return OrganizationOnboardingSaveResult.Invalid(
                    $"Account must be {MaximumBankValueLength} characters or fewer.");
            }
        }

        return OrganizationOnboardingSaveResult.Success();
    }

    private static async Task<OrganizationOnboardingSaveResult>
        ValidateReferencesAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int organizationID,
            string section,
            SaveOrganizationOnboardingSettingsRequestDto request,
            CancellationToken cancellationToken)
    {
        if (section == "anonymous")
        {
            return await ValidateOwnedIDAsync(
                connection,
                transaction,
                "dbo.Donor",
                "EMADonorID",
                request.EMADonorIDForAnonymous,
                organizationID,
                "anonymous donor",
                cancellationToken);
        }

        if (section == "deposit")
        {
            var chequeValidation =
                await ValidateOwnedIDAsync(
                    connection,
                    transaction,
                    "dbo.OrganizationMethodOfGiving",
                    "EMAMethodOfGivingID",
                    request.EMAMethodOfGivingIDForCheques,
                    organizationID,
                    "cheque method",
                    cancellationToken);

            if (!chequeValidation.Succeeded)
            {
                return chequeValidation;
            }

            return await ValidateOwnedIDAsync(
                connection,
                transaction,
                "dbo.OrganizationMethodOfGiving",
                "EMAMethodOfGivingID",
                request.EMAMethodOfGivingIDForCash,
                organizationID,
                "cash method",
                cancellationToken);
        }

        var values =
            section switch
            {
                "etransfers" =>
                    (
                        request.EMAEventIDForEtransfers,
                        request.EMASourceIDForEtransfers,
                        request.EMAMethodOfGivingIDForETransfers,
                        request.EMAFundIDForEtransfer,
                        request.EMACampusIDForEtransfers
                    ),

                "pads" =>
                    (
                        request.EMAEventIDForPADs,
                        request.EMASourceIDForPADs,
                        request.EMAMethodOfGivingIDForPADs,
                        request.EMAFundIDForPADs,
                        request.EMACampusIDForPADs
                    ),

                "crdb" =>
                    (
                        request.EMAEventIDForCreditDebitCards,
                        request.EMASourceIDForCreditDebitCards,
                        request.EMAMethodOfGivingIDForCreditDebitCards,
                        request.EMAFundIDForCreditDebitCards,
                        request.EMACampusIDForCreditDebitCards
                    ),

                _ =>
                    (
                        (int?)null,
                        (int?)null,
                        (int?)null,
                        (int?)null,
                        (int?)null
                    )
            };

        var checks =
            new[]
            {
                new LookupReference(
                    "dbo.OrganizationEvent",
                    "EMAEventID",
                    values.Item1,
                    "event"),

                new LookupReference(
                    "dbo.OrganizationDonationSource",
                    "EMADonationSourceID",
                    values.Item2,
                    "source"),

                new LookupReference(
                    "dbo.OrganizationMethodOfGiving",
                    "EMAMethodOfGivingID",
                    values.Item3,
                    "method of giving"),

                new LookupReference(
                    "dbo.OrganizationFund",
                    "EMAFundID",
                    values.Item4,
                    "fund"),

                new LookupReference(
                    "dbo.OrganizationCampus",
                    "EMACampusID",
                    values.Item5,
                    "campus")
            };

        foreach (var check in checks)
        {
            var validation =
                await ValidateOwnedIDAsync(
                    connection,
                    transaction,
                    check.Table,
                    check.IDColumn,
                    check.ID,
                    organizationID,
                    check.Label,
                    cancellationToken);

            if (!validation.Succeeded)
            {
                return validation;
            }
        }

        return OrganizationOnboardingSaveResult.Success();
    }

    private static async Task<OrganizationOnboardingSaveResult>
        ValidateOwnedIDAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string table,
            string idColumn,
            int? id,
            int organizationID,
            string label,
            CancellationToken cancellationToken)
    {
        if (id is null or <= 0)
        {
            return OrganizationOnboardingSaveResult.Success();
        }

        var sql =
            $"SELECT TOP (1) 1 FROM {table} WHERE {idColumn} = @ID AND EMAOrganizationID = @OrganizationID;";

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@ID",
                SqlDbType.Int)
            .Value = id.Value;

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        return await command.ExecuteScalarAsync(
                   cancellationToken) is not null
            ? OrganizationOnboardingSaveResult.Success()
            : OrganizationOnboardingSaveResult.Invalid(
                $"The selected {label} does not belong to this organization.");
    }

    private static async Task<int> UpdateSectionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int organizationID,
        int actorUserID,
        string section,
        SaveOrganizationOnboardingSettingsRequestDto request,
        CancellationToken cancellationToken)
    {
        var sql =
            section switch
            {
                "anonymous" =>
                    """
                    UPDATE dbo.Organization
                    SET EMADonorIDForAnonymous = @DonorID,
                        DateTimeUpdated = SYSUTCDATETIME(),
                        UpdatedByUserID = @UpdatedByUserID
                    WHERE EMAOrganizationID = @OrganizationID;
                    """,

                "deposit" =>
                    """
                    UPDATE dbo.Organization
                    SET BankBranchNumber = @BankBranchNumber,
                        BankAccountNumber = @BankAccountNumber,
                        EMAMethodOfGivingIDForCheques = @ChequeMethodID,
                        EMAMethodOfGivingIDForCash = @CashMethodID,
                        DateTimeUpdated = SYSUTCDATETIME(),
                        UpdatedByUserID = @UpdatedByUserID
                    WHERE EMAOrganizationID = @OrganizationID;
                    """,

                "etransfers" =>
                    """
                    UPDATE dbo.Organization
                    SET EMAEventIDForEtransfers = @EventID,
                        EMASourceIDForEtransfers = @SourceID,
                        EMAMethodOfGivingIDForETransfers = @MethodID,
                        EMAFundIDForEtransfer = @FundID,
                        EMACampusIDForEtransfers = @CampusID,
                        DateTimeUpdated = SYSUTCDATETIME(),
                        UpdatedByUserID = @UpdatedByUserID
                    WHERE EMAOrganizationID = @OrganizationID;
                    """,

                "pads" =>
                    """
                    UPDATE dbo.Organization
                    SET EMAEventIDForPADs = @EventID,
                        EMASourceIDForPADs = @SourceID,
                        EMAMethodOfGivingIDForPADs = @MethodID,
                        EMAFundIDForPADs = @FundID,
                        EMACampusIDForPADs = @CampusID,
                        DateTimeUpdated = SYSUTCDATETIME(),
                        UpdatedByUserID = @UpdatedByUserID
                    WHERE EMAOrganizationID = @OrganizationID;
                    """,

                "crdb" =>
                    """
                    UPDATE dbo.Organization
                    SET EMAEventIDForCreditDebitCards = @EventID,
                        EMASourceIDForCreditDebitCards = @SourceID,
                        EMAMethodOfGivingIDForCreditDebitCards = @MethodID,
                        EMAFundIDForCreditDebitCards = @FundID,
                        EMACampusIDForCreditDebitCards = @CampusID,
                        DateTimeUpdated = SYSUTCDATETIME(),
                        UpdatedByUserID = @UpdatedByUserID
                    WHERE EMAOrganizationID = @OrganizationID;
                    """,

                _ =>
                    throw new InvalidOperationException(
                        "Unsupported onboarding section.")
            };

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        command.Parameters
            .Add(
                "@UpdatedByUserID",
                SqlDbType.Int)
            .Value = actorUserID;

        switch (section)
        {
            case "anonymous":
                AddNullableInt(
                    command,
                    "@DonorID",
                    request.EMADonorIDForAnonymous);
                break;

            case "deposit":
                AddNullableString(
                    command,
                    "@BankBranchNumber",
                    request.BankBranchNumber,
                    MaximumBankValueLength);

                AddNullableString(
                    command,
                    "@BankAccountNumber",
                    request.BankAccountNumber,
                    MaximumBankValueLength);

                AddNullableInt(
                    command,
                    "@ChequeMethodID",
                    request.EMAMethodOfGivingIDForCheques);

                AddNullableInt(
                    command,
                    "@CashMethodID",
                    request.EMAMethodOfGivingIDForCash);
                break;

            case "etransfers":
                BindDonationChannel(
                    command,
                    request.EMAEventIDForEtransfers,
                    request.EMASourceIDForEtransfers,
                    request.EMAMethodOfGivingIDForETransfers,
                    request.EMAFundIDForEtransfer,
                    request.EMACampusIDForEtransfers);
                break;

            case "pads":
                BindDonationChannel(
                    command,
                    request.EMAEventIDForPADs,
                    request.EMASourceIDForPADs,
                    request.EMAMethodOfGivingIDForPADs,
                    request.EMAFundIDForPADs,
                    request.EMACampusIDForPADs);
                break;

            case "crdb":
                BindDonationChannel(
                    command,
                    request.EMAEventIDForCreditDebitCards,
                    request.EMASourceIDForCreditDebitCards,
                    request.EMAMethodOfGivingIDForCreditDebitCards,
                    request.EMAFundIDForCreditDebitCards,
                    request.EMACampusIDForCreditDebitCards);
                break;
        }

        return await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static void BindDonationChannel(
        SqlCommand command,
        int? eventID,
        int? sourceID,
        int? methodID,
        int? fundID,
        int? campusID)
    {
        AddNullableInt(
            command,
            "@EventID",
            eventID);

        AddNullableInt(
            command,
            "@SourceID",
            sourceID);

        AddNullableInt(
            command,
            "@MethodID",
            methodID);

        AddNullableInt(
            command,
            "@FundID",
            fundID);

        AddNullableInt(
            command,
            "@CampusID",
            campusID);
    }

    private static async Task<bool> OrganizationExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int organizationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.Organization
            WHERE EMAOrganizationID = @OrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        return await command.ExecuteScalarAsync(
                   cancellationToken) is not null;
    }

    private static async Task<bool> HasEtransferEmailAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int organizationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE
                WHEN NULLIF(
                        LTRIM(
                            RTRIM(
                                EtransferEmailAddress)),
                        '') IS NULL
                    THEN 0
                ELSE 1
            END
            FROM dbo.Organization
            WHERE EMAOrganizationID = @OrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)
                   ?? 0) == 1;
    }

    internal static async Task<bool> HasCreditDebitSchemaAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.Organization')
              AND name IN
              (
                  'EMAEventIDForCreditDebitCards',
                  'EMASourceIDForCreditDebitCards',
                  'EMAMethodOfGivingIDForCreditDebitCards',
                  'EMAFundIDForCreditDebitCards',
                  'EMACampusIDForCreditDebitCards'
              );
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)) == 5;
    }

    private static bool GetUseFlag(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return !reader.IsDBNull(
                   ordinal) &&
               string.Equals(
                   reader.GetValue(
                           ordinal)
                       ?.ToString()
                       ?.Trim(),
                   "Y",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int? GetNullableInt(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(
                   ordinal)
            ? null
            : Convert.ToInt32(
                reader.GetValue(
                    ordinal));
    }

    private static string? GetNullableString(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(
                   ordinal)
            ? null
            : reader.GetString(
                    ordinal)
                .Trim();
    }

    private static void AddNullableInt(
        SqlCommand command,
        string parameterName,
        int? value)
    {
        command.Parameters
            .Add(
                parameterName,
                SqlDbType.Int)
            .Value =
                value is > 0
                    ? value.Value
                    : DBNull.Value;
    }

    private static void AddNullableString(
        SqlCommand command,
        string parameterName,
        string? value,
        int maximumLength)
    {
        var normalized =
            string.IsNullOrWhiteSpace(
                value)
                ? null
                : value.Trim();

        command.Parameters
            .Add(
                parameterName,
                SqlDbType.VarChar,
                maximumLength)
            .Value =
                normalized is null
                    ? DBNull.Value
                    : normalized;
    }

    private sealed record LookupReference(
        string Table,
        string IDColumn,
        int? ID,
        string Label);
}
