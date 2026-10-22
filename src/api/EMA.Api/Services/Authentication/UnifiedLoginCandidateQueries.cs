using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using EMA.Api.DTOs;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Services.Authentication;

internal enum UnifiedLoginCandidateKind
{
    OrganizationUser = 1,
    Donor = 2
}

internal sealed class UnifiedLoginCandidate
{
    public UnifiedLoginCandidateKind Kind { get; init; }

    public int EMAOrganizationID { get; init; }

    public string OrganizationName { get; init; } =
        string.Empty;

    public int? UserID { get; init; }

    public string UserName { get; init; } =
        string.Empty;

    public int? DonorID { get; init; }

    public string DonorName { get; init; } =
        string.Empty;

    public string EmailAddress { get; init; } =
        string.Empty;
}

internal static class UnifiedLoginCandidateQueries
{
    public static int CalculateChallengeActorID(
        NormalizedLoginIdentifier identifier,
        string normalizedEmail)
    {
        var value =
            Encoding.UTF8.GetBytes(
                $"{(int)identifier.Kind}:{identifier.Value}\n{normalizedEmail}");

        var hash =
            SHA256.HashData(
                value);

        var actorID =
            BinaryPrimitives
                .ReadInt32BigEndian(
                    hash.AsSpan(
                        0,
                        sizeof(int)))
            & int.MaxValue;

        return actorID == 0
            ? 1
            : actorID;
    }

    public static async Task<List<UnifiedLoginCandidate>>
        GetCandidatesAsync(
            SqlConnection connection,
            NormalizedLoginIdentifier identifier,
            int platformOrganizationID,
            CancellationToken cancellationToken)
    {
        var identifierPredicateForUser =
            identifier.Kind ==
            LoginIdentifierKind.EmailAddress
                ? """
                    LOWER(
                        LTRIM(
                            RTRIM(
                                ISNULL(
                                    appUser.EmailAddress,
                                    '')))) =
                    @Identifier
                    """
                : """
                    RIGHT(
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    REPLACE(
                                        REPLACE(
                                            REPLACE(
                                                ISNULL(
                                                    appUser.PhoneNumber,
                                                    ''),
                                                '(',
                                                ''),
                                            ')',
                                            ''),
                                        '-',
                                        ''),
                                    ' ',
                                    ''),
                                '.',
                                ''),
                            '+',
                            ''),
                        10) =
                    @Identifier
                    """;

        var identifierPredicateForDonor =
            identifier.Kind ==
            LoginIdentifierKind.EmailAddress
                ? """
                    LOWER(
                        LTRIM(
                            RTRIM(
                                ISNULL(
                                    donor.EmailAddress,
                                    '')))) =
                    @Identifier
                    """
                : """
                    RIGHT(
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    REPLACE(
                                        REPLACE(
                                            REPLACE(
                                                ISNULL(
                                                    donor.PhoneNumber,
                                                    ''),
                                                '(',
                                                ''),
                                            ')',
                                            ''),
                                        '-',
                                        ''),
                                    ' ',
                                    ''),
                                '.',
                                ''),
                            '+',
                            ''),
                        10) =
                    @Identifier
                    """;

        var sql =
            $"""
            SELECT DISTINCT
                CAST(1 AS INT) AS CandidateKind,
                organization.EMAOrganizationID,
                organization.OrganizationName,
                appUser.UserID,
                appUser.UserName,
                CAST(NULL AS INT) AS DonorID,
                CAST('' AS NVARCHAR(200)) AS DonorName,
                LOWER(
                    LTRIM(
                        RTRIM(
                            ISNULL(
                                appUser.EmailAddress,
                                '')))) AS EmailAddress
            FROM dbo.[User] appUser
            INNER JOIN dbo.OrganizationUser organizationUser
                ON appUser.UserID =
                   organizationUser.UserID
            INNER JOIN dbo.Organization organization
                ON organizationUser.EMAOrganizationID =
                   organization.EMAOrganizationID
            WHERE appUser.IsActive = 1
              AND organizationUser.OrganizationUserStatusID = 10
              AND appUser.SandboxOrganizationID IS NULL
              AND ISNULL(
                      organization.OrganizationStatusID,
                      -1) <> 0
              AND {identifierPredicateForUser}
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.OrganizationUserRole organizationUserRole
                  INNER JOIN dbo.[Role] role
                      ON role.RoleID =
                         organizationUserRole.RoleID
                  WHERE organizationUserRole.EMAOrganizationID =
                        organizationUser.EMAOrganizationID
                    AND organizationUserRole.UserID =
                        organizationUser.UserID
                    AND ISNULL(
                            role.IsActive,
                            'Y') = 'Y'
              )

            UNION ALL

            SELECT
                CAST(2 AS INT) AS CandidateKind,
                organization.EMAOrganizationID,
                organization.OrganizationName,
                CAST(NULL AS INT) AS UserID,
                CAST('' AS NVARCHAR(200)) AS UserName,
                donor.EMADonorID AS DonorID,
                COALESCE(
                    NULLIF(
                        LTRIM(
                            RTRIM(
                                donor.OfficialDonorName)),
                        ''),
                    NULLIF(
                        LTRIM(
                            RTRIM(
                                donor.InteracDonorName)),
                        ''),
                    'Donor') AS DonorName,
                LOWER(
                    LTRIM(
                        RTRIM(
                            ISNULL(
                                donor.EmailAddress,
                                '')))) AS EmailAddress
            FROM dbo.Donor donor
            INNER JOIN dbo.Organization organization
                ON donor.EMAOrganizationID =
                   organization.EMAOrganizationID
            INNER JOIN dbo.[Role] donorRole
                ON donorRole.RoleID =
                   organization.DonorRoleID
            WHERE organization.EMAOrganizationID <>
                  @PlatformOrganizationID
              AND ISNULL(
                      organization.OrganizationStatusID,
                      -1) <> 0
              AND ISNULL(
                      donorRole.IsActive,
                      'Y') = 'Y'
              AND {identifierPredicateForDonor};
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@Identifier",
            SqlDbType.NVarChar,
            identifier.Kind ==
            LoginIdentifierKind.EmailAddress
                ? 320
                : 10)
            .Value =
                identifier.Value;

        command.Parameters.Add(
            "@PlatformOrganizationID",
            SqlDbType.Int)
            .Value =
                platformOrganizationID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var candidates =
            new List<UnifiedLoginCandidate>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var rawEmail =
                reader["EmailAddress"]
                    ?.ToString()
                ?? string.Empty;

            string normalizedEmail;

            try
            {
                normalizedEmail =
                    EmailAddressNormalizer
                        .Normalize(
                            rawEmail);
            }
            catch (ArgumentException)
            {
                continue;
            }

            candidates.Add(
                new UnifiedLoginCandidate
                {
                    Kind =
                        (UnifiedLoginCandidateKind)
                        Convert.ToInt32(
                            reader["CandidateKind"]),

                    EMAOrganizationID =
                        Convert.ToInt32(
                            reader["EMAOrganizationID"]),

                    OrganizationName =
                        reader["OrganizationName"]
                            ?.ToString()
                        ?? string.Empty,

                    UserID =
                        reader["UserID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                reader["UserID"]),

                    UserName =
                        reader["UserName"]
                            ?.ToString()
                        ?? string.Empty,

                    DonorID =
                        reader["DonorID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                reader["DonorID"]),

                    DonorName =
                        reader["DonorName"]
                            ?.ToString()
                        ?? string.Empty,

                    EmailAddress =
                        normalizedEmail
                });
        }

        return candidates;
    }

    public static List<UnifiedOrganizationUserChoiceDto>
        GetUserChoices(
            IEnumerable<UnifiedLoginCandidate> candidates,
            string emailAddress)
    {
        return candidates
            .Where(
                candidate =>
                    candidate.Kind ==
                    UnifiedLoginCandidateKind
                        .OrganizationUser &&
                    string.Equals(
                        candidate.EmailAddress,
                        emailAddress,
                        StringComparison.OrdinalIgnoreCase))
            .Select(
                candidate =>
                    new UnifiedOrganizationUserChoiceDto
                    {
                        UserID =
                            candidate.UserID
                            ?? 0,

                        UserName =
                            candidate.UserName,

                        EMAOrganizationID =
                            candidate.EMAOrganizationID,

                        OrganizationName =
                            candidate.OrganizationName
                    })
            .DistinctBy(
                choice =>
                    new
                    {
                        choice.UserID,
                        choice.EMAOrganizationID
                    })
            .OrderBy(
                choice =>
                    choice.UserName)
            .ThenBy(
                choice =>
                    choice.OrganizationName)
            .ToList();
    }

    public static List<UnifiedDonorChoiceDto>
        GetDonorChoices(
            IEnumerable<UnifiedLoginCandidate> candidates,
            string emailAddress)
    {
        return candidates
            .Where(
                candidate =>
                    candidate.Kind ==
                    UnifiedLoginCandidateKind
                        .Donor &&
                    string.Equals(
                        candidate.EmailAddress,
                        emailAddress,
                        StringComparison.OrdinalIgnoreCase))
            .Select(
                candidate =>
                    new UnifiedDonorChoiceDto
                    {
                        DonorID =
                            candidate.DonorID
                            ?? 0,

                        DonorName =
                            candidate.DonorName,

                        EMAOrganizationID =
                            candidate.EMAOrganizationID,

                        OrganizationName =
                            candidate.OrganizationName
                    })
            .DistinctBy(
                choice =>
                    new
                    {
                        choice.DonorID,
                        choice.EMAOrganizationID
                    })
            .OrderBy(
                choice =>
                    choice.OrganizationName)
            .ThenBy(
                choice =>
                    choice.DonorID)
            .ToList();
    }
}
