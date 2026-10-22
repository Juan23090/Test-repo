using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Navigation;

public interface IMenuItemAudioGenerationPersistence
{
    Task<bool> TableExistsAsync(
        CancellationToken cancellationToken);

    Task<MenuItemAudioResourceSnapshot?> GetAudioResourceAsync(
        int menuItemID,
        int menuItemRowID,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateGeneratedBlobAsync(
        int menuItemID,
        int menuItemRowID,
        string expectedAudioScript,
        string? expectedBlobID,
        string newBlobID,
        CancellationToken cancellationToken);
}

public sealed record MenuItemAudioResourceSnapshot(
    int MenuItemRowID,
    int MenuItemID,
    string MenuItemResourceTypeID,
    string? AudioScript,
    string? AudioVideoBlob);

internal sealed class SqlMenuItemAudioGenerationPersistence :
    IMenuItemAudioGenerationPersistence
{
    private readonly string _connectionString;

    public SqlMenuItemAudioGenerationPersistence(
        IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public async Task<bool> TableExistsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.MenuItemResource', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
            connection);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<MenuItemAudioResourceSnapshot?> GetAudioResourceAsync(
        int menuItemID,
        int menuItemRowID,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                MenuItemRowID,
                MenuItemID,
                MenuItemResourceTypeID,
                AudioScript,
                AudioVideoBlob
            FROM dbo.MenuItemResource
            WHERE MenuItemID = @MenuItemID
              AND MenuItemRowID = @MenuItemRowID;
            """;

        await using var command =
            new SqlCommand(sql, connection);
        command.Parameters.Add("@MenuItemID", SqlDbType.Int)
            .Value = menuItemID;
        command.Parameters.Add("@MenuItemRowID", SqlDbType.Int)
            .Value = menuItemRowID;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new MenuItemAudioResourceSnapshot(
            Convert.ToInt32(reader["MenuItemRowID"]),
            Convert.ToInt32(reader["MenuItemID"]),
            reader["MenuItemResourceTypeID"]?.ToString()?.Trim()
                ?? string.Empty,
            reader["AudioScript"] is DBNull
                ? null
                : reader["AudioScript"]?.ToString(),
            reader["AudioVideoBlob"] is DBNull
                ? null
                : reader["AudioVideoBlob"]?.ToString());
    }

    public async Task<bool> TryUpdateGeneratedBlobAsync(
        int menuItemID,
        int menuItemRowID,
        string expectedAudioScript,
        string? expectedBlobID,
        string newBlobID,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.MenuItemResource
            SET AudioVideoBlob = @NewBlobID
            WHERE MenuItemID = @MenuItemID
              AND MenuItemRowID = @MenuItemRowID
              AND UPPER(MenuItemResourceTypeID) = 'A'
              AND AudioScript = @ExpectedAudioScript
              AND
              (
                  (AudioVideoBlob IS NULL AND @ExpectedBlobID IS NULL)
                  OR AudioVideoBlob = @ExpectedBlobID
              );

            SELECT @@ROWCOUNT;
            """;

        await using var command =
            new SqlCommand(sql, connection);
        command.Parameters.Add("@MenuItemID", SqlDbType.Int)
            .Value = menuItemID;
        command.Parameters.Add("@MenuItemRowID", SqlDbType.Int)
            .Value = menuItemRowID;
        command.Parameters.Add("@ExpectedAudioScript", SqlDbType.VarChar, -1)
            .Value = expectedAudioScript;
        command.Parameters.Add("@ExpectedBlobID", SqlDbType.VarChar, 36)
            .Value = (object?)expectedBlobID ?? DBNull.Value;
        command.Parameters.Add("@NewBlobID", SqlDbType.VarChar, 36)
            .Value = newBlobID;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
