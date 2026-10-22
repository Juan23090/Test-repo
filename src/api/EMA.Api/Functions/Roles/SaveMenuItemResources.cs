using System.Data;
using EMA.Api.DTOs;
using EMA.Api.Services.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Roles;

public sealed class SaveMenuItemResources
{
    private readonly ILogger<SaveMenuItemResources> _logger;
    private readonly IMenuItemResourcePersistence _persistence;

    public SaveMenuItemResources(ILogger<SaveMenuItemResources> logger)
        : this(logger, new SqlMenuItemResourcePersistence(
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.")))
    {
    }

    internal SaveMenuItemResources(
        ILogger<SaveMenuItemResources> logger,
        IMenuItemResourcePersistence persistence)
    {
        _logger = logger;
        _persistence = persistence;
    }

    [Function("SaveMenuItemResources")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu-items/{menuItemID:int}/resources")]
        HttpRequest request,
        int menuItemID)
    {
        if (menuItemID <= 0)
            return new BadRequestObjectResult("A valid MenuItem ID is required.");

        try
        {
            var payload = await request.ReadFromJsonAsync<SaveMenuItemResourcesRequest>(
                cancellationToken: request.HttpContext.RequestAborted);

            if (payload is null)
                return new BadRequestObjectResult("MenuItem resource data is required.");

            var cancellationToken = request.HttpContext.RequestAborted;

            if (!await _persistence.TablesExistAsync(cancellationToken))
            {
                return new ObjectResult(new
                {
                    message = "MenuItem resource tables are unavailable in this database, so resources cannot be saved."
                }) { StatusCode = StatusCodes.Status409Conflict };
            }

            var menuItem = await _persistence.GetMenuItemAsync(menuItemID, cancellationToken);
            if (!menuItem.Exists)
                return new NotFoundObjectResult($"MenuItem {menuItemID} was not found.");

            var normalizedResources = new List<MenuItemResourceDto>();
            foreach (var resource in payload.Resources ?? new List<MenuItemResourceDto>())
            {
                if (!TryNormalizeResource(
                        menuItemID,
                        menuItem.Url,
                        resource,
                        out var normalizedResource,
                        out var validationMessage))
                {
                    return new BadRequestObjectResult(
                        validationMessage ?? "One or more MenuItem resources are invalid.");
                }

                normalizedResources.Add(normalizedResource!);
            }

            var saved = await _persistence.ReplaceAsync(
                menuItemID,
                normalizedResources,
                cancellationToken);

            return new OkObjectResult(saved);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error saving MenuItem resources for MenuItem {MenuItemID}.",
                menuItemID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private static bool TryNormalizeResource(
        int menuItemID,
        string? menuUrl,
        MenuItemResourceDto resource,
        out MenuItemResourceDto? normalizedResource,
        out string? validationMessage)
    {
        normalizedResource = null;
        validationMessage = null;

        var typeID = resource.MenuItemResourceTypeID?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(typeID) ||
            typeID.Length != 1 ||
            resource.Sequence < 0 ||
            resource.Sequence > short.MaxValue ||
            string.IsNullOrWhiteSpace(resource.Description) ||
            resource.Description.Trim().Length > 256)
        {
            validationMessage = "Each resource needs a valid type, sequence, and description.";
            return false;
        }

        string? audioScript = null;
        string? mediaReference = null;
        string? screenUrl = null;

        switch (typeID)
        {
            case "A":
                if (!TryNormalizeMediaReference(resource.AudioVideoBlob, out mediaReference))
                {
                    validationMessage = "Audio resources need a valid EMA blob ID when a blob is supplied.";
                    return false;
                }
                audioScript = NormalizeAudioScript(resource.AudioScript);
                if (string.IsNullOrWhiteSpace(mediaReference) && string.IsNullOrWhiteSpace(audioScript))
                {
                    validationMessage = "Audio resources require either an EMA blob ID or an Audio Script.";
                    return false;
                }
                break;

            case "V":
                if (!TryNormalizeMediaReference(resource.AudioVideoBlob, out mediaReference) ||
                    string.IsNullOrWhiteSpace(mediaReference))
                {
                    validationMessage = "Video resources require a valid EMA blob ID.";
                    return false;
                }
                break;

            case "W":
                var candidateScreenUrl = resource.ScreenURL;
                if (string.IsNullOrWhiteSpace(candidateScreenUrl))
                    candidateScreenUrl = BuildConventionalSetupUrl(menuUrl);
                if (!TryNormalizeInternalScreenUrl(candidateScreenUrl, out screenUrl) ||
                    string.IsNullOrWhiteSpace(screenUrl))
                {
                    validationMessage = "Wrench resources require a valid internal setup Screen URL.";
                    return false;
                }
                break;

            default:
                if (!TryNormalizeMediaReference(resource.AudioVideoBlob, out mediaReference) ||
                    !TryNormalizeInternalScreenUrl(resource.ScreenURL, out screenUrl))
                {
                    validationMessage = "The resource contains an invalid media or screen URL value.";
                    return false;
                }
                break;
        }

        normalizedResource = new MenuItemResourceDto
        {
            MenuItemRowID = resource.MenuItemRowID,
            MenuItemID = menuItemID,
            MenuItemResourceTypeID = typeID,
            Sequence = resource.Sequence,
            Description = resource.Description.Trim(),
            AudioScript = audioScript,
            AudioVideoBlob = mediaReference,
            ScreenURL = screenUrl,
            ResourceTypeDescription = resource.ResourceTypeDescription,
            ResourceTypeName = resource.ResourceTypeName,
            RawSVG = resource.RawSVG
        };
        return true;
    }

    private static string? NormalizeAudioScript(string? rawScript) =>
        string.IsNullOrWhiteSpace(rawScript) ? null : rawScript.Trim();

    private static bool TryNormalizeInternalScreenUrl(string? rawUrl, out string? normalizedUrl)
    {
        normalizedUrl = null;
        if (string.IsNullOrWhiteSpace(rawUrl)) return true;
        var candidate = rawUrl.Trim();
        if (candidate.Length > 256 || candidate.Any(char.IsControl) || candidate.Contains('\\') ||
            candidate.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate(candidate, UriKind.Absolute, out _))
            return false;
        normalizedUrl = candidate.StartsWith("/", StringComparison.Ordinal) ? candidate : $"/{candidate}";
        return true;
    }

    private static bool TryNormalizeMediaReference(string? rawReference, out string? normalizedReference)
    {
        normalizedReference = null;
        if (string.IsNullOrWhiteSpace(rawReference)) return true;
        var candidate = rawReference.Trim();
        if (candidate.Length > 36 || candidate.Any(char.IsControl) || candidate.Contains('/') ||
            candidate.Contains('\\') || Uri.TryCreate(candidate, UriKind.Absolute, out _))
            return false;
        normalizedReference = candidate;
        return true;
    }

    private static string? BuildConventionalSetupUrl(string? menuUrl)
    {
        if (string.IsNullOrWhiteSpace(menuUrl) || Uri.TryCreate(menuUrl, UriKind.Absolute, out _)) return null;
        var normalized = menuUrl.Trim().TrimEnd('/');
        return normalized.EndsWith("-setup", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}-setup";
    }

    public sealed class SaveMenuItemResourcesRequest
    {
        public List<MenuItemResourceDto> Resources { get; set; } = new();
    }
}

internal interface IMenuItemResourcePersistence
{
    Task<bool> TablesExistAsync(CancellationToken cancellationToken);
    Task<MenuItemLookupResult> GetMenuItemAsync(int menuItemID, CancellationToken cancellationToken);
    Task<IReadOnlyList<MenuItemResourceDto>> ReplaceAsync(
        int menuItemID,
        IReadOnlyList<MenuItemResourceDto> resources,
        CancellationToken cancellationToken);
}

internal sealed record MenuItemLookupResult(bool Exists, string? Url);

internal sealed class SqlMenuItemResourcePersistence : IMenuItemResourcePersistence
{
    private readonly string _connectionString;

    public SqlMenuItemResourcePersistence(string connectionString) => _connectionString = connectionString;

    public async Task<bool> TablesExistAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await MenuItemResourceQuery.TablesExistAsync(connection, cancellationToken: cancellationToken);
    }

    public async Task<MenuItemLookupResult> GetMenuItemAsync(int menuItemID, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT URL FROM dbo.MenuItem WHERE MenuItemID = @MenuItemID", connection);
        command.Parameters.Add("@MenuItemID", SqlDbType.Int).Value = menuItemID;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new MenuItemLookupResult(false, null);
        return new MenuItemLookupResult(
            true,
            reader["URL"] is DBNull ? null : reader["URL"]?.ToString());
    }

    public async Task<IReadOnlyList<MenuItemResourceDto>> ReplaceAsync(
        int menuItemID,
        IReadOnlyList<MenuItemResourceDto> resources,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var deleteCommand = new SqlCommand(
                "DELETE FROM dbo.MenuItemResource WHERE MenuItemID = @MenuItemID", connection, transaction))
            {
                deleteCommand.Parameters.Add("@MenuItemID", SqlDbType.Int).Value = menuItemID;
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            const string insertSql = """
                INSERT INTO dbo.MenuItemResource
                (MenuItemID, MenuItemResourceTypeID, Sequence, Description, AudioScript, AudioVideoBlob, ScreenURL)
                OUTPUT INSERTED.MenuItemRowID
                VALUES
                (@MenuItemID, @MenuItemResourceTypeID, @Sequence, @Description, @AudioScript, @AudioVideoBlob, @ScreenURL);
                """;

            foreach (var resource in resources.OrderBy(resource => resource.Sequence))
            {
                await using var insertCommand = new SqlCommand(insertSql, connection, transaction);
                AddResourceParameters(insertCommand, menuItemID, resource);
                resource.MenuItemRowID = Convert.ToInt32(
                    await insertCommand.ExecuteScalarAsync(cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return resources
                .OrderBy(resource => resource.Sequence)
                .ThenBy(resource => resource.MenuItemRowID)
                .ToList();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void AddResourceParameters(SqlCommand command, int menuItemID, MenuItemResourceDto resource)
    {
        command.Parameters.Add("@MenuItemID", SqlDbType.Int).Value = menuItemID;
        command.Parameters.Add("@MenuItemResourceTypeID", SqlDbType.Char, 1).Value = resource.MenuItemResourceTypeID;
        command.Parameters.Add("@Sequence", SqlDbType.SmallInt).Value = resource.Sequence;
        command.Parameters.Add("@Description", SqlDbType.VarChar, 256).Value = resource.Description;
        command.Parameters.Add("@AudioScript", SqlDbType.VarChar, -1).Value = (object?)resource.AudioScript ?? DBNull.Value;
        command.Parameters.Add("@AudioVideoBlob", SqlDbType.VarChar, 36).Value = (object?)resource.AudioVideoBlob ?? DBNull.Value;
        command.Parameters.Add("@ScreenURL", SqlDbType.VarChar, 256).Value = (object?)resource.ScreenURL ?? DBNull.Value;
    }
}
