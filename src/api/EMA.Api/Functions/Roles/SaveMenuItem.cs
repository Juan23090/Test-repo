using System.Data;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Roles;

public class SaveMenuItem
{
    private readonly ILogger<SaveMenuItem> _logger;
    private readonly string _connectionString;

    public SaveMenuItem(
        ILogger<SaveMenuItem> logger)
    {
        _logger = logger;
        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("SaveMenuItem")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "menu-items/save")]
        HttpRequest request)
    {
        try
        {
            var menuItem =
                await request.ReadFromJsonAsync<SaveMenuItemDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (menuItem is null ||
                menuItem.MenuItemID <= 0 ||
                string.IsNullOrWhiteSpace(menuItem.ScreenTitle) ||
                !TryNormalizeDisplayControlID(
                    menuItem.DisplayControlID,
                    out var normalizedDisplayControlID) ||
                !HasValidLandingCoordinates(
                    menuItem.Row,
                    menuItem.MenuItemTypeID,
                    normalizedDisplayControlID) ||
                (normalizedDisplayControlID != "L" &&
                 string.IsNullOrWhiteSpace(menuItem.MenuName)) ||
                !TryNormalizeMenuUrl(
                    menuItem.URL,
                    out var normalizedUrl))
            {
                return new BadRequestObjectResult(
                    "One or more menu item values are invalid.");
            }

            menuItem.DisplayControlID = normalizedDisplayControlID;
            menuItem.URL = normalizedUrl;
            menuItem.IsActive =
                string.Equals(
                    menuItem.IsActive,
                    "Y",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Y"
                    : "N";

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync(
                request.HttpContext.RequestAborted);

            var isPublicCategory = false;

            if (menuItem.MenuItemCategoryID.HasValue)
            {
                await using var categoryCommand =
                    new SqlCommand(
                        "SELECT Description FROM dbo.MenuItemCategory WHERE MenuItemCategoryID = @CatID",
                        connection);

                categoryCommand.Parameters.Add(
                    "@CatID",
                    SqlDbType.SmallInt)
                    .Value = menuItem.MenuItemCategoryID.Value;

                var categoryDescription =
                    await categoryCommand.ExecuteScalarAsync(
                        request.HttpContext.RequestAborted)
                    as string;

                isPublicCategory =
                    string.Equals(
                        categoryDescription,
                        "Public",
                        StringComparison.OrdinalIgnoreCase);
            }

            if (!isPublicCategory &&
                (menuItem.RoleIDs is null ||
                 menuItem.RoleIDs.Count == 0))
            {
                return new BadRequestObjectResult(
                    "At least one role must be selected.");
            }

            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync(
                    request.HttpContext.RequestAborted);

            try
            {
                var targetID =
                    menuItem.OriginalMenuItemID.HasValue &&
                    menuItem.OriginalMenuItemID.Value > 0
                        ? menuItem.OriginalMenuItemID.Value
                        : menuItem.MenuItemID;

                var exists =
                    await MenuItemExistsAsync(
                        connection,
                        transaction,
                        targetID,
                        request.HttpContext.RequestAborted);

                if (exists)
                {
                    const string updateSql = """
                        UPDATE dbo.MenuItem
                        SET MenuItemID = @MenuItemID,
                            MenuItemCategoryID = @MenuItemCategoryID,
                            MenuItemRowID = @MenuItemRowID,
                            MenuItemTypeID = @MenuItemTypeID,
                            MenuName = @MenuName,
                            ScreenTitle = @ScreenTitle,
                            DisplayControlID = @DisplayControlID,
                            URL = @URL,
                            IsActive = @IsActive
                        WHERE MenuItemID = @TargetID;
                        """;

                    await using var updateCommand =
                        new SqlCommand(
                            updateSql,
                            connection,
                            transaction);

                    AddMenuItemParameters(
                        updateCommand,
                        menuItem);

                    updateCommand.Parameters.Add(
                        "@TargetID",
                        SqlDbType.Int)
                        .Value = targetID;

                    await updateCommand.ExecuteNonQueryAsync(
                        request.HttpContext.RequestAborted);
                }
                else
                {
                    const string insertSql = """
                        INSERT INTO dbo.MenuItem
                        (
                            MenuItemID,
                            MenuItemCategoryID,
                            MenuItemRowID,
                            MenuItemTypeID,
                            MenuName,
                            ScreenTitle,
                            DisplayControlID,
                            URL,
                            IsActive
                        )
                        VALUES
                        (
                            @MenuItemID,
                            @MenuItemCategoryID,
                            @MenuItemRowID,
                            @MenuItemTypeID,
                            @MenuName,
                            @ScreenTitle,
                            @DisplayControlID,
                            @URL,
                            @IsActive
                        );
                        """;

                    await using var insertCommand =
                        new SqlCommand(
                            insertSql,
                            connection,
                            transaction);

                    AddMenuItemParameters(
                        insertCommand,
                        menuItem);

                    await insertCommand.ExecuteNonQueryAsync(
                        request.HttpContext.RequestAborted);
                }

                await ReplaceRoleMappingsAsync(
                    connection,
                    transaction,
                    menuItem,
                    request.HttpContext.RequestAborted);

                await transaction.CommitAsync(
                    request.HttpContext.RequestAborted);

                return new OkResult();
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(
                    request.HttpContext.RequestAborted);

                _logger.LogError(
                    exception,
                    "Transaction failed for SaveMenuItem.");

                throw;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error saving menu item.");

            return new StatusCodeResult(
                StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<bool> MenuItemExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int menuItemID,
        CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(
                "SELECT COUNT(1) FROM dbo.MenuItem WHERE MenuItemID = @MenuItemID",
                connection,
                transaction);

        command.Parameters.Add(
            "@MenuItemID",
            SqlDbType.Int)
            .Value = menuItemID;

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)) > 0;
    }

    private static async Task ReplaceRoleMappingsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaveMenuItemDto menuItem,
        CancellationToken cancellationToken)
    {
        await using (var deleteCommand =
            new SqlCommand(
                "DELETE FROM dbo.RoleMenuItem WHERE MenuItemID = @MenuItemID",
                connection,
                transaction))
        {
            deleteCommand.Parameters.Add(
                "@MenuItemID",
                SqlDbType.Int)
                .Value = menuItem.MenuItemID;

            await deleteCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        foreach (var roleID in menuItem.RoleIDs.Distinct())
        {
            await using var insertCommand =
                new SqlCommand(
                    "INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID) VALUES (@RoleID, @MenuItemID)",
                    connection,
                    transaction);

            insertCommand.Parameters.Add(
                "@RoleID",
                SqlDbType.Int)
                .Value = roleID;

            insertCommand.Parameters.Add(
                "@MenuItemID",
                SqlDbType.Int)
                .Value = menuItem.MenuItemID;

            await insertCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static void AddMenuItemParameters(
        SqlCommand command,
        SaveMenuItemDto menuItem)
    {
        command.Parameters.Add(
            "@MenuItemID",
            SqlDbType.Int)
            .Value = menuItem.MenuItemID;

        command.Parameters.Add(
            "@MenuItemCategoryID",
            SqlDbType.SmallInt)
            .Value =
                (object?)menuItem.MenuItemCategoryID
                ?? DBNull.Value;

        command.Parameters.Add(
            "@MenuItemRowID",
            SqlDbType.SmallInt)
            .Value = menuItem.Row;

        command.Parameters.Add(
            "@MenuItemTypeID",
            SqlDbType.SmallInt)
            .Value = menuItem.MenuItemTypeID;

        command.Parameters.Add(
            "@MenuName",
            SqlDbType.NVarChar,
            200)
            .Value = menuItem.MenuName.Trim();

        command.Parameters.Add(
            "@ScreenTitle",
            SqlDbType.NVarChar,
            200)
            .Value = menuItem.ScreenTitle.Trim();

        command.Parameters.Add(
            "@DisplayControlID",
            SqlDbType.Char,
            1)
            .Value = menuItem.DisplayControlID;

        command.Parameters.Add(
            "@URL",
            SqlDbType.NVarChar,
            500)
            .Value =
                (object?)menuItem.URL
                ?? DBNull.Value;

        command.Parameters.Add(
            "@IsActive",
            SqlDbType.Char,
            1)
            .Value = menuItem.IsActive ?? "N";
    }

    private static bool HasValidLandingCoordinates(
        short row,
        short menuItemTypeID,
        string displayControlID)
    {
        if (displayControlID == "L")
        {
            return row == 0 &&
                   menuItemTypeID == 0;
        }

        return row > 0 &&
               menuItemTypeID > 0;
    }

    private static bool TryNormalizeDisplayControlID(
        string? rawValue,
        out string normalizedValue)
    {
        var candidate = rawValue?.Trim().ToUpperInvariant();

        if (candidate is not ("M" or "W" or "B" or "L"))
        {
            normalizedValue = string.Empty;
            return false;
        }

        normalizedValue = candidate;
        return true;
    }

    private static bool TryNormalizeMenuUrl(
        string? rawUrl,
        out string? normalizedUrl)
    {
        normalizedUrl = null;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return true;
        }

        var candidate = rawUrl.Trim();

        if (candidate.Length > 500 ||
            candidate.Any(char.IsControl) ||
            candidate.Contains('\\') ||
            candidate.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.TryCreate(
                candidate,
                UriKind.Absolute,
                out var absoluteUri))
        {
            if (absoluteUri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            normalizedUrl = absoluteUri.AbsoluteUri;
            return true;
        }

        normalizedUrl =
            candidate.StartsWith("/", StringComparison.Ordinal)
                ? candidate
                : $"/{candidate}";

        return true;
    }

    public sealed class SaveMenuItemDto
    {
        public int MenuItemID { get; set; }

        [JsonPropertyName("originalMenuItemID")]
        public int? OriginalMenuItemID { get; set; }

        public short? MenuItemCategoryID { get; set; }
        public short Row { get; set; }
        public short MenuItemTypeID { get; set; }
        public string DisplayControlID { get; set; } = "M";
        public string MenuName { get; set; } = string.Empty;
        public string ScreenTitle { get; set; } = string.Empty;
        public string? URL { get; set; }

        [JsonPropertyName("isActive")]
        public string? IsActive { get; set; }

        public List<int> RoleIDs { get; set; } = new();
    }
}
