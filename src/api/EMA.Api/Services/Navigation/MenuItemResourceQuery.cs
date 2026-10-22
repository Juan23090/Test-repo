using System.Data;
using EMA.Api.DTOs;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Services.Navigation;

public static class MenuItemResourceQuery
{
    public static async Task<bool> TablesExistAsync(
        SqlConnection connection,
        SqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE
                WHEN OBJECT_ID(N'dbo.MenuItemResource', N'U') IS NOT NULL
                 AND OBJECT_ID(N'dbo.MenuItemResourceType', N'U') IS NOT NULL
                    THEN 1
                ELSE 0
            END;
            """;

        await using var command =
            new SqlCommand(sql, connection, transaction);

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public static async Task<Dictionary<int, List<MenuItemResourceDto>>> LoadAsync(
        SqlConnection connection,
        IEnumerable<int> menuItemIDs,
        CancellationToken cancellationToken = default)
    {
        var ids = menuItemIDs
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var result =
            new Dictionary<int, List<MenuItemResourceDto>>();

        if (ids.Length == 0 ||
            !await TablesExistAsync(
                connection,
                cancellationToken: cancellationToken))
        {
            return result;
        }

        var parameterNames =
            ids.Select((_, index) => $"@MenuItemID{index}")
               .ToArray();

        var sql = $"""
            SELECT
                mir.MenuItemRowID,
                mir.MenuItemID,
                mir.MenuItemResourceTypeID,
                mir.Sequence,
                mir.Description,
                mir.AudioScript,
                mir.AudioVideoBlob,
                mir.ScreenURL,
                mirt.Description AS ResourceTypeDescription,
                mirt.Name AS ResourceTypeName,
                mirt.RawSVG
            FROM dbo.MenuItemResource mir
            INNER JOIN dbo.MenuItemResourceType mirt
                ON mir.MenuItemResourceTypeID =
                   mirt.MenuItemResourceTypeID
            WHERE mir.MenuItemID IN ({string.Join(", ", parameterNames)})
            ORDER BY
                mir.MenuItemID,
                CASE UPPER(mir.MenuItemResourceTypeID)
                    WHEN 'V' THEN 0
                    WHEN 'A' THEN 1
                    WHEN 'W' THEN 2
                    ELSE 3
                END,
                mir.Sequence,
                mir.MenuItemRowID;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        for (var index = 0; index < ids.Length; index++)
        {
            command.Parameters.Add(
                parameterNames[index],
                SqlDbType.Int)
                .Value = ids[index];
        }

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var menuItemID =
                Convert.ToInt32(reader["MenuItemID"]);

            if (!result.TryGetValue(menuItemID, out var resources))
            {
                resources = new List<MenuItemResourceDto>();
                result[menuItemID] = resources;
            }

            resources.Add(new MenuItemResourceDto
            {
                MenuItemRowID =
                    Convert.ToInt32(reader["MenuItemRowID"]),
                MenuItemID = menuItemID,
                MenuItemResourceTypeID =
                    reader["MenuItemResourceTypeID"]?.ToString()?.Trim()
                    ?? string.Empty,
                Sequence =
                    Convert.ToInt32(reader["Sequence"]),
                Description =
                    reader["Description"]?.ToString()
                    ?? string.Empty,
                AudioScript =
                    reader["AudioScript"] is DBNull
                        ? null
                        : reader["AudioScript"]?.ToString(),
                AudioVideoBlob =
                    reader["AudioVideoBlob"] is DBNull
                        ? null
                        : reader["AudioVideoBlob"]?.ToString(),
                ScreenURL =
                    reader["ScreenURL"] is DBNull
                        ? null
                        : reader["ScreenURL"]?.ToString(),
                ResourceTypeDescription =
                    reader["ResourceTypeDescription"]?.ToString()
                    ?? string.Empty,
                ResourceTypeName =
                    reader["ResourceTypeName"]?.ToString()
                    ?? string.Empty,
                RawSVG =
                    reader["RawSVG"] is DBNull
                        ? null
                        : reader["RawSVG"]?.ToString()
            });
        }

        return result;
    }
}
