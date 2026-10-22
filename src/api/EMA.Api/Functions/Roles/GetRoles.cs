using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Roles;

public class GetRoles
{
    private readonly ILogger<GetRoles> _logger;
    private readonly string _connectionString;
    private readonly ApiRequestBoundaryValidator
        _authorization;

    public GetRoles(
        ILogger<GetRoles> logger,
        ApiRequestBoundaryValidator authorization)
    {
        _logger = logger;
        _authorization =
            authorization;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetRoles")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "roles")] HttpRequest req)
    {
        _logger.LogInformation("GetRoles triggered.");
        
        var roles = new List<RoleDto>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var identity =
                req.GetRequiredApiIdentity();

            const string sql = """
                SELECT
                    role.RoleID,
                    role.RoleName
                FROM dbo.[Role] role
                WHERE ISNULL(
                          role.IsActive,
                          'Y') = 'Y'
                  AND
                  (
                      role.RoleID <> @InsiderRoleID
                      OR @EMAOrganizationID =
                         @PlatformOrganizationID
                  )
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.Organization
                          donorRoleOrganization
                      WHERE donorRoleOrganization
                                .EMAOrganizationID =
                            @EMAOrganizationID
                        AND donorRoleOrganization
                                .DonorRoleID =
                            role.RoleID
                  )
                  AND
                  (
                      @IsPlatformAdministrator = 1
                      OR NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.RoleMenuItem
                              roleMenuItem
                          INNER JOIN dbo.MenuItem
                              menuItem
                              ON menuItem.MenuItemID =
                                 roleMenuItem.MenuItemID
                          WHERE roleMenuItem.RoleID =
                                role.RoleID
                            AND menuItem.MenuItemCategoryID =
                                @PlatformAdministratorCategoryID
                            AND ISNULL(
                                    menuItem.IsActive,
                                    'Y') = 'Y'
                      )
                  )
                ORDER BY role.RoleName;
                """;

            using var cmd =
                new SqlCommand(
                    sql,
                    conn);

            cmd.Parameters.Add(
                "@EMAOrganizationID",
                System.Data.SqlDbType.Int)
                .Value =
                    identity.EMAOrganizationID;

            cmd.Parameters.Add(
                "@InsiderRoleID",
                System.Data.SqlDbType.Int)
                .Value =
                    9000;

            cmd.Parameters.Add(
                "@PlatformOrganizationID",
                System.Data.SqlDbType.Int)
                .Value =
                    _authorization
                        .PlatformOrganizationID;

            cmd.Parameters.Add(
                "@IsPlatformAdministrator",
                System.Data.SqlDbType.Bit)
                .Value =
                    _authorization
                        .IsPlatformAdministrator(
                            identity);

            cmd.Parameters.Add(
                "@PlatformAdministratorCategoryID",
                System.Data.SqlDbType.Int)
                .Value =
                    _authorization
                        .PlatformAdministratorCategoryID;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                roles.Add(new RoleDto
                {
                    RoleID = reader.GetInt32(0),
                    RoleName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                });
            }

            return new OkObjectResult(roles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching roles.");
            return new StatusCodeResult(500);
        }
    }

    public class RoleDto
    {
        public int RoleID { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }
}
