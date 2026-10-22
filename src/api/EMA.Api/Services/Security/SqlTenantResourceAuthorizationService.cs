using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class
    SqlTenantResourceAuthorizationService :
        ITenantResourceAuthorizationService
{
    private const int MaximumReferences = 100;

    private static readonly
        IReadOnlyDictionary<
            string,
            TenantResourceDefinition>
        NamedResources =
            new Dictionary<
                string,
                TenantResourceDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["EMADonorID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.Donor",
                            "EMADonorID",
                            "EMAOrganizationID"),

                ["DonorID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.Donor",
                            "EMADonorID",
                            "EMAOrganizationID"),

                ["EMADonorIDForAnonymous"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.Donor",
                            "EMADonorID",
                            "EMAOrganizationID"),

                ["EMADonationID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.Donation",
                            "EMADonationID",
                            "EMAOrganizationID"),

                ["DonationID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.Donation",
                            "EMADonationID",
                            "EMAOrganizationID"),

                ["EMACampusID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationCampus",
                            "EMACampusID",
                            "EMAOrganizationID"),

                ["EMADefaultCampusID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationCampus",
                            "EMACampusID",
                            "EMAOrganizationID"),

                ["EMAFundID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationFund",
                            "EMAFundID",
                            "EMAOrganizationID"),

                ["EMAMethodOfGivingID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationMethodOfGiving",
                            "EMAMethodOfGivingID",
                            "EMAOrganizationID"),

                ["EMACurrencyTypeID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationCurrencyType",
                            "EMACurrencyTypeID",
                            "EMAOrganizationID"),

                ["EMADonationSourceID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationDonationSource",
                            "EMADonationSourceID",
                            "EMAOrganizationID"),

                ["EMAEventID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationEvent",
                            "EMAEventID",
                            "EMAOrganizationID"),

                ["EMAInteractionTypeID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationInteractionType",
                            "EMAInteractionTypeID",
                            "EMAOrganizationID"),

                ["EMATributeTypeID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.OrganizationTributeType",
                            "EMATributeTypeID",
                            "EMAOrganizationID"),

                ["DonorStatusID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.DonorStatus",
                            "DonorStatusID",
                            "EMAOrganizationID"),

                ["ActivityTypeID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.DonorJourneyActivityType",
                            "ActivityTypeID",
                            "EMAOrganizationID"),

                ["FollowUpActivityTypeID"] =
                    TenantResourceDefinition
                        .Direct(
                            "dbo.DonorJourneyActivityType",
                            "ActivityTypeID",
                            "EMAOrganizationID"),

                ["UserID"] =
                    new TenantResourceDefinition(
                        """
                        SELECT CASE
                            WHEN EXISTS
                            (
                                SELECT 1
                                FROM dbo.OrganizationUser
                                WHERE UserID = @ResourceID
                                  AND EMAOrganizationID =
                                      @EMAOrganizationID
                                  AND OrganizationUserStatusID = 10
                            )
                            THEN 1
                            ELSE 0
                        END;
                        """)
            };

    private static readonly
        IReadOnlyDictionary<
            string,
            TenantResourceDefinition>
        RouteIDResources =
            BuildRouteIDResources();

    private readonly string _connectionString;

    public SqlTenantResourceAuthorizationService(
        IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public async Task<bool>
        AreResourcesOwnedByTenantAsync(
            string functionName,
            IReadOnlyDictionary<string, object?>
                bindingData,
            IQueryCollection query,
            JsonElement? requestBody,
            ApiRequestIdentity identity,
            CancellationToken cancellationToken = default)
    {
        var references =
            new HashSet<
                TenantResourceReference>();

        CollectBindingReferences(
            functionName,
            bindingData,
            references);

        CollectQueryReferences(
            query,
            references);

        if (requestBody.HasValue)
        {
            CollectJsonReferences(
                requestBody.Value,
                references);

            CollectFunctionSpecificJsonReferences(
                functionName,
                requestBody.Value,
                references);
        }

        if (references.Count >
            MaximumReferences)
        {
            return false;
        }

        if (references.Count == 0)
        {
            return true;
        }

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        foreach (var reference in
                 references)
        {
            await using var command =
                new SqlCommand(
                    reference.Definition.Sql,
                    connection);

            command.Parameters.Add(
                "@ResourceID",
                SqlDbType.BigInt)
                .Value =
                    reference.ResourceID;

            command.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value =
                    identity
                        .EMAOrganizationID;

            var result =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            if (Convert.ToInt32(
                    result,
                    CultureInfo.InvariantCulture) !=
                1)
            {
                return false;
            }
        }

        return true;
    }

    private static void
        CollectBindingReferences(
            string functionName,
            IReadOnlyDictionary<string, object?>
                bindingData,
            ISet<TenantResourceReference>
                references)
    {
        foreach (var pair in bindingData)
        {
            if (!TryParsePositiveID(
                    pair.Value?.ToString(),
                    out var resourceID))
            {
                continue;
            }

            if (pair.Key.Equals(
                    "id",
                    StringComparison.OrdinalIgnoreCase) &&
                RouteIDResources.TryGetValue(
                    functionName,
                    out var routeDefinition))
            {
                references.Add(
                    new TenantResourceReference(
                        routeDefinition,
                        resourceID));

                continue;
            }

            if (NamedResources.TryGetValue(
                    pair.Key,
                    out var namedDefinition))
            {
                references.Add(
                    new TenantResourceReference(
                        namedDefinition,
                        resourceID));
            }
        }
    }

    private static void
        CollectQueryReferences(
            IQueryCollection query,
            ISet<TenantResourceReference>
                references)
    {
        foreach (var pair in query)
        {
            if (!NamedResources.TryGetValue(
                    pair.Key,
                    out var definition))
            {
                continue;
            }

            foreach (var value in pair.Value)
            {
                if (TryParsePositiveID(
                        value,
                        out var resourceID))
                {
                    references.Add(
                        new TenantResourceReference(
                            definition,
                            resourceID));
                }
            }
        }
    }

    private static void CollectJsonReferences(
        JsonElement element,
        ISet<TenantResourceReference>
            references)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in
                         element.EnumerateObject())
                {
                    if (NamedResources
                            .TryGetValue(
                                property.Name,
                                out var definition) &&
                        TryGetPositiveID(
                            property.Value,
                            out var resourceID))
                    {
                        references.Add(
                            new TenantResourceReference(
                                definition,
                                resourceID));
                    }

                    CollectJsonReferences(
                        property.Value,
                        references);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in
                         element.EnumerateArray())
                {
                    CollectJsonReferences(
                        item,
                        references);
                }

                break;
        }
    }

    private static void
        CollectFunctionSpecificJsonReferences(
            string functionName,
            JsonElement requestBody,
            ISet<TenantResourceReference>
                references)
    {
        if (!functionName.Equals(
                "MergeData",
                StringComparison.Ordinal) ||
            requestBody.ValueKind !=
                JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(
                requestBody,
                "EntityType",
                out var entityTypeElement) ||
            entityTypeElement.ValueKind !=
                JsonValueKind.String)
        {
            return;
        }

        var definition =
            entityTypeElement
                .GetString()?
                .Trim()
                .ToLowerInvariant()
            switch
            {
                "donors" =>
                    NamedResources[
                        "EMADonorID"],

                "funds" =>
                    NamedResources[
                        "EMAFundID"],

                "givingmethods" or
                "giving methods" =>
                    NamedResources[
                        "EMAMethodOfGivingID"],

                "sources" or
                "source" =>
                    NamedResources[
                        "EMADonationSourceID"],

                _ => null
            };

        if (definition is null)
        {
            return;
        }

        foreach (var propertyName in
                 new[]
                 {
                     "FromID",
                     "ToID"
                 })
        {
            if (TryGetPropertyIgnoreCase(
                    requestBody,
                    propertyName,
                    out var idElement) &&
                TryGetPositiveID(
                    idElement,
                    out var resourceID))
            {
                references.Add(
                    new TenantResourceReference(
                        definition,
                        resourceID));
            }
        }
    }

    private static bool
        TryGetPropertyIgnoreCase(
            JsonElement element,
            string propertyName,
            out JsonElement value)
    {
        foreach (var property in
                 element.EnumerateObject())
        {
            if (property.Name.Equals(
                    propertyName,
                    StringComparison
                        .OrdinalIgnoreCase))
            {
                value =
                    property.Value;

                return true;
            }
        }

        value =
            default;

        return false;
    }

    private static bool TryGetPositiveID(
        JsonElement element,
        out long value)
    {
        value = 0;

        return element.ValueKind switch
        {
            JsonValueKind.Number =>
                element.TryGetInt64(
                    out value) &&
                value > 0,

            JsonValueKind.String =>
                TryParsePositiveID(
                    element.GetString(),
                    out value),

            _ => false
        };
    }

    private static bool TryParsePositiveID(
        string? rawValue,
        out long value)
    {
        return long.TryParse(
                   rawValue,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value > 0;
    }

    private static IReadOnlyDictionary<
        string,
        TenantResourceDefinition>
        BuildRouteIDResources()
    {
        var donor =
            NamedResources[
                "EMADonorID"];

        var donation =
            NamedResources[
                "EMADonationID"];

        return new Dictionary<
            string,
            TenantResourceDefinition>(
            StringComparer.Ordinal)
        {
            ["GetDonorById"] = donor,
            ["UpdateDonor"] = donor,
            ["UpdateDonorJourneyDetails"] =
                donor,

            ["EditDonation"] = donation,
            ["GetDonationAudit"] = donation,
            ["GetDonationByID"] = donation,
            ["GetDonationWIP"] = donation,
            ["GetEmailDonation"] = donation,
            ["PostEmailAcknowledge"] =
                donation,
            ["PostEmailConfirm"] = donation,
            ["PostEmailSave"] = donation,
            ["SaveDonation"] = donation,
            ["UpdateDonationWIP"] =
                donation,
            ["UpdateDonation"] = donation,

            ["DeleteCampus"] =
                NamedResources[
                    "EMACampusID"],
            ["PutCampus"] =
                NamedResources[
                    "EMACampusID"],

            ["DeleteFund"] =
                NamedResources[
                    "EMAFundID"],
            ["PutFund"] =
                NamedResources[
                    "EMAFundID"],

            ["DeleteMethodOfGiving"] =
                NamedResources[
                    "EMAMethodOfGivingID"],
            ["PutMethodOfGiving"] =
                NamedResources[
                    "EMAMethodOfGivingID"],

            ["PutDonationSource"] =
                NamedResources[
                    "EMADonationSourceID"],
            ["PutEvent"] =
                NamedResources[
                    "EMAEventID"],
            ["PutInteractionType"] =
                NamedResources[
                    "EMAInteractionTypeID"],
            ["PutTributeType"] =
                NamedResources[
                    "EMATributeTypeID"],
            ["PutDonorStatus"] =
                NamedResources[
                    "DonorStatusID"]
        };
    }

    private sealed record
        TenantResourceReference(
            TenantResourceDefinition Definition,
            long ResourceID);

    private sealed record
        TenantResourceDefinition(
            string Sql)
    {
        public static TenantResourceDefinition
            Direct(
                string tableName,
                string idColumn,
                string organizationColumn)
        {
            return new TenantResourceDefinition(
                $"""
                 SELECT CASE
                     WHEN EXISTS
                     (
                         SELECT 1
                         FROM {tableName}
                         WHERE {idColumn} =
                               @ResourceID
                           AND {organizationColumn} =
                               @EMAOrganizationID
                     )
                     THEN 1
                     ELSE 0
                 END;
                 """);
        }
    }
}
