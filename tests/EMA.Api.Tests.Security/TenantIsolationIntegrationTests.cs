using System.Text.Json;
using EMA.Api.Functions.Donations;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Tests.Security;

/*
 * Tenant-isolation coverage for the donation write endpoints.
 *
 * The protections these assert are SQL predicates, not in-process
 * logic, so they cannot be exercised without a database. Set
 * EMA_TEST_SQL_CONNECTION to run them; without it every test here is
 * skipped, which keeps CI green on machines with no database.
 *
 * A deliberately separate variable from SqlConnectionString so these
 * never run against whatever database happens to be configured for the
 * API.
 *
 * Seed data: src/db/seed_tenant_isolation_test_org.sql
 *
 * Every assertion is read-only.
 */
public sealed class TenantIsolationIntegrationTests
{
    private const string ConnectionVariable =
        "EMA_TEST_SQL_CONNECTION";

    private const string TestOrganizationName =
        "ZZ Tenant Isolation Test Org";

    [IntegrationFact]
    public async Task DonationOwnedByAnotherOrganizationIsNotVisible()
    {
        await using var connection =
            await OpenAsync();

        var other =
            await LoadTestOrganizationAsync(
                connection);

        var donationID =
            await ScalarAsync<long?>(
                connection,
                """
                SELECT TOP (1) EMADonationID
                FROM dbo.Donation
                WHERE EMAOrganizationID = @OrganizationID;
                """,
                other.OrganizationID);

        Assert.NotNull(donationID);

        await using var transaction =
            (SqlTransaction)await connection
                .BeginTransactionAsync();

        // Asking as a different organization must find nothing, which is
        // what makes UpdateDonation return 404 rather than touching it.
        var donation =
            await DonationFunctionHelpers.GetDonationAsync(
                connection,
                transaction,
                other.OrganizationID + 1,
                donationID!.Value,
                CancellationToken.None);

        Assert.Null(donation);

        // Sanity: the row really is readable by its owner.
        var ownersView =
            await DonationFunctionHelpers.GetDonationAsync(
                connection,
                transaction,
                other.OrganizationID,
                donationID.Value,
                CancellationToken.None);

        Assert.NotNull(ownersView);

        await transaction.RollbackAsync();
    }

    [IntegrationFact]
    public async Task DonorOwnedByAnotherOrganizationCannotBeAssigned()
    {
        await using var connection =
            await OpenAsync();

        var other =
            await LoadTestOrganizationAsync(
                connection);

        var donorID =
            await ScalarAsync<int?>(
                connection,
                """
                SELECT TOP (1) EMADonorID
                FROM dbo.Donor
                WHERE EMAOrganizationID = @OrganizationID;
                """,
                other.OrganizationID);

        Assert.NotNull(donorID);

        await using var transaction =
            (SqlTransaction)await connection
                .BeginTransactionAsync();

        // Null here is what produces the 400 "The selected donor does not
        // belong to this organization."
        var foreignDonorName =
            await DonationFunctionHelpers.GetDonorNameAsync(
                connection,
                transaction,
                other.OrganizationID + 1,
                donorID!.Value,
                CancellationToken.None);

        Assert.Null(foreignDonorName);

        var ownersDonorName =
            await DonationFunctionHelpers.GetDonorNameAsync(
                connection,
                transaction,
                other.OrganizationID,
                donorID.Value,
                CancellationToken.None);

        Assert.NotNull(ownersDonorName);

        await transaction.RollbackAsync();
    }

    [IntegrationFact]
    public async Task CrossOrganizationFundInDistributionsIsDenied()
    {
        await using var connection =
            await OpenAsync();

        var other =
            await LoadTestOrganizationAsync(
                connection);

        var fundID =
            await ScalarAsync<int?>(
                connection,
                """
                SELECT TOP (1) EMAFundID
                FROM dbo.OrganizationFund
                WHERE EMAOrganizationID = @OrganizationID;
                """,
                other.OrganizationID);

        Assert.NotNull(fundID);

        var service =
            new SqlTenantResourceAuthorizationService(
                BuildConfiguration());

        // The fund is nested inside Distributions, which is where a
        // cross-tenant reference is easiest to hide.
        var body =
            JsonDocument.Parse(
                $$"""
                {
                    "Distributions": [
                        {
                            "EMAFundID": {{fundID}},
                            "DistributionAmount": 5.00
                        }
                    ]
                }
                """).RootElement;

        var allowedForOwner =
            await service.AreResourcesOwnedByTenantAsync(
                "CreateDonation",
                new Dictionary<string, object?>(),
                new QueryCollection(),
                body,
                CreateIdentity(
                    other.OrganizationID),
                CancellationToken.None);

        var allowedForOutsider =
            await service.AreResourcesOwnedByTenantAsync(
                "CreateDonation",
                new Dictionary<string, object?>(),
                new QueryCollection(),
                body,
                CreateIdentity(
                    other.OrganizationID + 1),
                CancellationToken.None);

        Assert.False(
            allowedForOutsider,
            "A fund belonging to another organization must be rejected.");

        // The fund is the only reference in the body, so the owning
        // organization must be allowed. Without this the test would still
        // pass if the service rejected everything unconditionally.
        Assert.True(
            allowedForOwner,
            "The owning organization must be allowed its own fund.");
    }

    private static ApiRequestIdentity CreateIdentity(
        int organizationID)
    {
        var issued =
            DateTimeOffset.UtcNow;

        return new ApiRequestIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.OrganizationUser,
            organizationID,
            UserID: 13,
            DonorID: null,
            RoleIDs: Array.Empty<int>(),
            AllowedCategoryIDs: new[] { 10, 20 },
            IssuedAtUtc: issued,
            ExpiresAtUtc: issued.AddHours(1));
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SqlConnectionString"] =
                        ConnectionString
                })
            .Build();
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            ConnectionVariable)
        ?? throw new InvalidOperationException(
            $"{ConnectionVariable} is not set.");

    private static async Task<SqlConnection> OpenAsync()
    {
        var connection =
            new SqlConnection(
                ConnectionString);

        await connection.OpenAsync();

        return connection;
    }

    private static async Task<(int OrganizationID, string Name)>
        LoadTestOrganizationAsync(
            SqlConnection connection)
    {
        var organizationID =
            await ScalarAsync<int?>(
                connection,
                """
                SELECT TOP (1) EMAOrganizationID
                FROM dbo.Organization
                WHERE OrganizationName = @OrganizationID;
                """,
                TestOrganizationName);

        Assert.True(
            organizationID.HasValue,
            $"Seed '{TestOrganizationName}' first: run src/db/seed_tenant_isolation_test_org.sql.");

        return (organizationID!.Value, TestOrganizationName);
    }

    private static async Task<T?> ScalarAsync<T>(
        SqlConnection connection,
        string sql,
        object parameterValue)
    {
        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "@OrganizationID",
            parameterValue);

        var result =
            await command.ExecuteScalarAsync();

        if (result is null ||
            result is DBNull)
        {
            return default;
        }

        return (T)Convert.ChangeType(
            result,
            Nullable.GetUnderlyingType(
                typeof(T)) ??
            typeof(T));
    }
}

/// <summary>
/// A Fact that skips unless a test database is configured.
/// </summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "EMA_TEST_SQL_CONNECTION")))
        {
            Skip =
                "Set EMA_TEST_SQL_CONNECTION to run tenant-isolation integration tests.";
        }
    }
}
