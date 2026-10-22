using System.Data;
using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;

internal static class DonationFunctionHelpers
{
    internal const int OfficeNotificationLoadedStepID = 115;
    internal const int OfficeNotificationSavedStepID = 120;
    internal const int ManuallyKeyedDonationCreatedStepID = 500;
    internal const int ManuallyEditedDonationStepID = 520;

    internal static void ValidateDonation(
        DonationDTO donation)
    {
        if (donation.EMADonorID <= 0)
        {
            throw new ArgumentException(
                "A donor is required.");
        }

        if (!donation.DateTimeReceived.HasValue)
        {
            throw new ArgumentException(
                "A donation date is required.");
        }

        donation.ReferenceNumber =
            string.IsNullOrWhiteSpace(donation.ReferenceNumber)
                ? null
                : donation.ReferenceNumber.Trim();

        if (donation.ReferenceNumber?.Length > 255)
        {
            throw new ArgumentException(
                "The reference number cannot exceed 255 characters.");
        }

        if (donation.DonationAmount <= 0)
        {
            throw new ArgumentException(
                "The donation amount must be greater than zero.");
        }

        var distributions =
            donation.Distributions
                .Where(distribution =>
                    distribution.DistributionAmount != 0)
                .ToList();

        if (distributions.Count == 0)
        {
            throw new ArgumentException(
                "At least one distribution amount is required.");
        }

        if (distributions.Any(distribution =>
                distribution.DistributionAmount < 0))
        {
            throw new ArgumentException(
                "Distribution amounts cannot be negative.");
        }

        var distributionTotal =
            decimal.Round(
                distributions.Sum(distribution =>
                    distribution.DistributionAmount),
                2,
                MidpointRounding.AwayFromZero);

        var donationTotal =
            decimal.Round(
                donation.DonationAmount,
                2,
                MidpointRounding.AwayFromZero);

        if (distributionTotal != donationTotal)
        {
            throw new ArgumentException(
                $"The distribution total ({distributionTotal:C}) must equal the donation amount ({donationTotal:C}).");
        }
    }

    internal static async Task<string?> GetDonorNameAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int orgID,
        int donorID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                COALESCE(
                    NULLIF(LTRIM(RTRIM(InteracDonorName)), ''),
                    NULLIF(LTRIM(RTRIM(OfficialDonorName)), ''))
            FROM dbo.Donor
            WHERE EMAOrganizationID = @EMAOrganizationID
              AND EMADonorID = @EMADonorID;
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
                "@EMADonorID",
                SqlDbType.Int)
            .Value =
                donorID;

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null or DBNull
            ? null
            : result.ToString();
    }

    internal static void AddDonationParameters(
        SqlCommand command,
        DonationDTO donation,
        string? donorName,
        int userID)
    {
        command.Parameters
            .Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
            .Value =
                donation.EMAOrganizationID;

        command.Parameters
            .Add(
                "@EMADonorID",
                SqlDbType.Int)
            .Value =
                donation.EMADonorID;

        command.Parameters
            .Add(
                "@InteracDonorName",
                SqlDbType.NVarChar,
                255)
            .Value =
                donation.ClearInteracDonorName
                    ? DBNull.Value
                    : NullableString(
                        donorName);

        command.Parameters
            .Add(
                "@DateTimeReceived",
                SqlDbType.DateTime2)
            .Value =
                donation.DateTimeReceived!.Value;

        command.Parameters
            .Add(
                "@EMADonationSourceID",
                SqlDbType.Int)
            .Value =
                NullablePositiveInt(
                    donation.EMADonationSourceID);

        command.Parameters
            .Add(
                "@EMAMethodOfGivingID",
                SqlDbType.Int)
            .Value =
                NullablePositiveInt(
                    donation.EMAMethodOfGivingID);

        command.Parameters
            .Add(
                "@EMAEventID",
                SqlDbType.Int)
            .Value =
                NullablePositiveInt(
                    donation.EMAEventID);

        command.Parameters
            .Add(
                "@ReferenceNumber",
                SqlDbType.NVarChar,
                255)
            .Value =
                NullableString(
                    donation.ReferenceNumber);

        command.Parameters
            .Add(
                "@Comments",
                SqlDbType.NVarChar,
                -1)
            .Value =
                NullableString(
                    donation.Comments);

        var donationAmount =
            command.Parameters.Add(
                "@DonationAmount",
                SqlDbType.Decimal);

        donationAmount.Precision =
            18;

        donationAmount.Scale =
            2;

        donationAmount.Value =
            donation.DonationAmount;

        command.Parameters
            .Add(
                "@TributeText",
                SqlDbType.NVarChar,
                -1)
            .Value =
                NullableString(
                    donation.TributeText);

        command.Parameters
            .Add(
                "@EMATributeTypeID",
                SqlDbType.Int)
            .Value =
                NullablePositiveInt(
                    donation.EMATributeTypeID);

        command.Parameters
            .Add(
                "@IsTemplate",
                SqlDbType.Char,
                1)
            .Value =
                NormalizeFlag(
                    donation.IsTemplate);

        command.Parameters
            .Add(
                "@UserID",
                SqlDbType.Int)
            .Value =
                userID;
    }

    internal static async Task ReplaceDistributionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long donationID,
        IEnumerable<DonationDistributionDTO> distributions,
        CancellationToken cancellationToken)
    {
        const string deleteSql = """
            DELETE FROM dbo.DonationDistribution
            WHERE EMADonationID = @EMADonationID;
            """;

        await using (var deleteCommand =
                     new SqlCommand(
                         deleteSql,
                         connection,
                         transaction))
        {
            deleteCommand.Parameters
                .Add(
                    "@EMADonationID",
                    SqlDbType.BigInt)
                .Value =
                    donationID;

            await deleteCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        const string insertSql = """
            INSERT INTO dbo.DonationDistribution
            (
                EMADonationID,
                EMACampusID,
                EMAFundID,
                DistributionAmount
            )
            VALUES
            (
                @EMADonationID,
                @EMACampusID,
                @EMAFundID,
                @DistributionAmount
            );
            """;

        foreach (var distribution in distributions.Where(
                     distribution =>
                         distribution.DistributionAmount > 0))
        {
            await using var insertCommand =
                new SqlCommand(
                    insertSql,
                    connection,
                    transaction);

            insertCommand.Parameters
                .Add(
                    "@EMADonationID",
                    SqlDbType.BigInt)
                .Value =
                    donationID;

            insertCommand.Parameters
                .Add(
                    "@EMACampusID",
                    SqlDbType.Int)
                .Value =
                    distribution.EMACampusID > 0
                        ? distribution.EMACampusID
                        : DBNull.Value;

            insertCommand.Parameters
                .Add(
                    "@EMAFundID",
                    SqlDbType.Int)
                .Value =
                    distribution.EMAFundID > 0
                        ? distribution.EMAFundID
                        : DBNull.Value;

            var amount =
                insertCommand.Parameters.Add(
                    "@DistributionAmount",
                    SqlDbType.Decimal);

            amount.Precision =
                18;

            amount.Scale =
                2;

            amount.Value =
                distribution.DistributionAmount;

            await insertCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    internal static async Task<DonationDTO?> GetDonationAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int orgID,
        long donationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                d.EMADonationID,
                d.EMAOrganizationID,
                d.EMADonorID,
                COALESCE(
                    NULLIF(d.InteracDonorName, ''),
                    NULLIF(dn.OfficialDonorName, ''),
                    dn.InteracDonorName) AS InteracDonorName,
                d.Comments,
                d.DateTimeReceived,
                d.DonorToOrgChangeRequest,
                d.EMADonationSourceID,
                d.EMAMethodOfGivingID,
                d.EMAEventID,
                d.GlobalMessageID,
                d.ReferenceNumber,
                d.DonationAmount,
                d.TributeText,
                d.EMATributeTypeID,
                d.IsTemplate,
                d.DateTimeCreated,
                d.CreatedbyUserID,
                d.DateTimeUpdated,
                d.UpdatedbyUserID
            FROM dbo.Donation d
            LEFT JOIN dbo.Donor dn
                ON d.EMADonorID = dn.EMADonorID
               AND d.EMAOrganizationID = dn.EMAOrganizationID
            WHERE d.EMAOrganizationID = @EMAOrganizationID
              AND d.EMADonationID = @EMADonationID;
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
                "@EMADonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        var donation =
            ReadDonation(
                reader);

        await reader.CloseAsync();

        donation.Distributions =
            await GetDistributionsAsync(
                connection,
                transaction,
                donationID,
                cancellationToken);

        return donation;
    }

    internal static async Task<List<DonationDistributionDTO>>
        GetDistributionsAsync(
            SqlConnection connection,
            SqlTransaction? transaction,
            long donationID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EMADonationDistributionID,
                EMADonationID,
                EMACampusID,
                EMAFundID,
                DistributionAmount
            FROM dbo.DonationDistribution
            WHERE EMADonationID = @EMADonationID
            ORDER BY EMADonationDistributionID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@EMADonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        var distributions =
            new List<DonationDistributionDTO>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            distributions.Add(
                new DonationDistributionDTO
                {
                    EMADonationDistributionID =
                        Convert.ToInt64(
                            reader.GetValue(
                                reader.GetOrdinal(
                                    "EMADonationDistributionID"))),

                    EMADonationID =
                        Convert.ToInt64(
                            reader.GetValue(
                                reader.GetOrdinal(
                                    "EMADonationID"))),

                    EMACampusID =
                        GetNullableInt(
                            reader,
                            "EMACampusID")
                        ?? 0,

                    EMAFundID =
                        GetNullableInt(
                            reader,
                            "EMAFundID")
                        ?? 0,

                    DistributionAmount =
                        reader.GetDecimal(
                            reader.GetOrdinal(
                                "DistributionAmount"))
                });
        }

        return distributions;
    }

    internal static DonationDTO ReadDonation(
        SqlDataReader reader) =>
        new()
        {
            EMADonationID =
                reader.GetInt64(
                    reader.GetOrdinal(
                        "EMADonationID")),

            EMAOrganizationID =
                reader.GetInt32(
                    reader.GetOrdinal(
                        "EMAOrganizationID")),

            EMADonorID =
                reader.GetInt32(
                    reader.GetOrdinal(
                        "EMADonorID")),

            InteracDonorName =
                GetNullableString(
                    reader,
                    "InteracDonorName"),

            Comments =
                GetNullableString(
                    reader,
                    "Comments"),

            DateTimeReceived =
                GetNullableDateTime(
                    reader,
                    "DateTimeReceived"),

            DonorToOrgChangeRequest =
                GetNullableString(
                    reader,
                    "DonorToOrgChangeRequest"),

            EMADonationSourceID =
                GetNullableInt(
                    reader,
                    "EMADonationSourceID"),

            EMAMethodOfGivingID =
                GetNullableInt(
                    reader,
                    "EMAMethodOfGivingID"),

            EMAEventID =
                GetNullableInt(
                    reader,
                    "EMAEventID"),

            GlobalMessageID =
                GetNullableString(
                    reader,
                    "GlobalMessageID"),

            ReferenceNumber =
                GetNullableString(
                    reader,
                    "ReferenceNumber"),

            DonationAmount =
                reader.GetDecimal(
                    reader.GetOrdinal(
                        "DonationAmount")),

            TributeText =
                GetNullableString(
                    reader,
                    "TributeText"),

            EMATributeTypeID =
                GetNullableInt(
                    reader,
                    "EMATributeTypeID"),

            IsTemplate =
                GetNullableFlag(
                    reader,
                    "IsTemplate"),

            DateTimeCreated =
                GetNullableDateTime(
                    reader,
                    "DateTimeCreated"),

            CreatedbyUserID =
                GetNullableInt(
                    reader,
                    "CreatedbyUserID"),

            DateTimeUpdated =
                GetNullableDateTime(
                    reader,
                    "DateTimeUpdated"),

            UpdatedbyUserID =
                GetNullableInt(
                    reader,
                    "UpdatedbyUserID")
        };

    internal static async Task LogStepAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        long donationID,
        int stepID,
        bool refreshLoggedTime,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF EXISTS
            (
                SELECT 1
                FROM dbo.DonationStep
                WHERE DonationStepID = @DonationStepID
            )
            BEGIN
                UPDATE dbo.DonationStepLog WITH (UPDLOCK, HOLDLOCK)
                SET DateTimeLogged = SYSUTCDATETIME()
                WHERE EMADonationID = @EMADonationID
                  AND DonationStepID = @DonationStepID
                  AND @RefreshLoggedTime = 1;

                IF @@ROWCOUNT = 0
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DonationStepLog WITH (UPDLOCK, HOLDLOCK)
                    WHERE EMADonationID = @EMADonationID
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
                        @EMADonationID,
                        @DonationStepID,
                        SYSUTCDATETIME()
                    );
                END;
            END;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.Add(
                "@EMADonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        command.Parameters.Add(
                "@DonationStepID",
                SqlDbType.Int)
            .Value =
                stepID;

        command.Parameters.Add(
                "@RefreshLoggedTime",
                SqlDbType.Bit)
            .Value =
                refreshLoggedTime;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    internal static async Task WriteAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DonationDTO existingDonation,
        int updatedByUserID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.DonationAudit
            (
                EMADonationID,
                DDDDJsonPayload,
                UpdatedByUserID,
                DateTimeCreated
            )
            VALUES
            (
                @EMADonationID,
                @JsonPayload,
                @UpdatedByUserID,
                @DateTimeCreated
            );
            """;

        var payload =
            JsonSerializer.Serialize(
                new
                {
                    Donation =
                        existingDonation,

                    Distributions =
                        existingDonation.Distributions
                });

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters
            .Add(
                "@EMADonationID",
                SqlDbType.BigInt)
            .Value =
                existingDonation.EMADonationID;

        command.Parameters
            .Add(
                "@JsonPayload",
                SqlDbType.NVarChar,
                -1)
            .Value =
                payload;

        command.Parameters
            .Add(
                "@UpdatedByUserID",
                SqlDbType.Int)
            .Value =
                updatedByUserID;

        command.Parameters
            .Add(
                "@DateTimeCreated",
                SqlDbType.DateTime2)
            .Value =
                DateTime.UtcNow;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    internal static bool IsDuplicateReferenceNumberException(
        SqlException exception)
    {
        ArgumentNullException.ThrowIfNull(
            exception);

        return exception.Errors
            .Cast<SqlError>()
            .Any(error =>
                IsDuplicateReferenceNumberError(
                    error.Number,
                    error.Message));
    }

    internal static bool IsDuplicateReferenceNumberError(
        int errorNumber,
        string message) =>
        errorNumber is 2601 or 2627 &&
        message.Contains(
            "UX_Donation_Organization_ReferenceNumber",
            StringComparison.Ordinal);

    private static object NullableString(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();

    private static object NullablePositiveInt(
        int? value) =>
        value.HasValue &&
        value.Value > 0
            ? value.Value
            : DBNull.Value;

    private static string NormalizeFlag(
        char? value) =>
        value is 'Y' or 'y' or (char)1
            ? "Y"
            : "N";

    private static string? GetNullableString(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetValue(ordinal)?.ToString();
    }

    private static int? GetNullableInt(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(
                reader.GetValue(ordinal));
    }

    private static long? GetNullableLong(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt64(
                reader.GetValue(ordinal));
    }

    private static DateTime? GetNullableDateTime(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetDateTime(ordinal);
    }

    private static char? GetNullableFlag(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var rawValue =
            reader.GetValue(ordinal);

        if (rawValue is bool booleanValue)
        {
            return booleanValue
                ? 'Y'
                : 'N';
        }

        var value =
            rawValue.ToString();

        return string.IsNullOrEmpty(value)
            ? null
            : value[0];
    }
}
