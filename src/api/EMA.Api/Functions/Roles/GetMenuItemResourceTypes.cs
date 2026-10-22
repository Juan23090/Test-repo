using System.Data;
using EMA.Api.DTOs;
using EMA.Api.Services.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Roles;

public sealed class GetMenuItemResourceTypes
{
    private readonly ILogger<GetMenuItemResourceTypes> _logger;
    private readonly string _connectionString;

    public GetMenuItemResourceTypes(
        ILogger<GetMenuItemResourceTypes> logger)
    {
        _logger = logger;
        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetMenuItemResourceTypes")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "menu-item-resource-types")]
        HttpRequest request)
    {
        try
        {
            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync(
                request.HttpContext.RequestAborted);

            if (!await MenuItemResourceQuery.TablesExistAsync(
                    connection,
                    cancellationToken:
                        request.HttpContext.RequestAborted))
            {
                return new OkObjectResult(
                    Array.Empty<MenuItemResourceTypeDto>());
            }

            const string sql = """
                SELECT
                    MenuItemResourceTypeID,
                    Description,
                    Name,
                    RawSVG
                FROM dbo.MenuItemResourceType
                ORDER BY CASE UPPER(MenuItemResourceTypeID)
                    WHEN 'V' THEN 0
                    WHEN 'A' THEN 1
                    WHEN 'W' THEN 2
                    ELSE 3
                END,
                MenuItemResourceTypeID;
                """;

            await using var command =
                new SqlCommand(sql, connection);

            var resourceTypes =
                new List<MenuItemResourceTypeDto>();

            await using var reader =
                await command.ExecuteReaderAsync(
                    request.HttpContext.RequestAborted);

            while (await reader.ReadAsync(
                       request.HttpContext.RequestAborted))
            {
                resourceTypes.Add(new MenuItemResourceTypeDto
                {
                    MenuItemResourceTypeID =
                        reader["MenuItemResourceTypeID"]?.ToString()?.Trim()
                        ?? string.Empty,
                    Description =
                        reader["Description"]?.ToString()
                        ?? string.Empty,
                    Name =
                        reader["Name"]?.ToString()
                        ?? string.Empty,
                    RawSVG =
                        reader["RawSVG"]?.ToString()
                        ?? string.Empty
                });
            }

            return new OkObjectResult(resourceTypes);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load MenuItem resource types.");

            return new ObjectResult(new
            {
                message =
                    "Unable to load MenuItem resource types."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
