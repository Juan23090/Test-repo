using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Users;

public class GetUsers
{
    private readonly ILogger<GetUsers> _logger;
    private readonly string _connectionString;

    public GetUsers(ILogger<GetUsers> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetUsers")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/{orgID:int}/users")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetUsers triggered for org {OrgID}.", orgID);
        
        var usersMap = new Dictionary<int, UserDto>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                @"SELECT u.UserID, u.UserName, u.EmailAddress, u.PhoneNumber, u.IsActive, ur.RoleID
                  FROM [dbo].[User] u
                  INNER JOIN [dbo].[OrganizationUser] ou ON u.UserID = ou.UserId
                  LEFT JOIN [dbo].[OrganizationUserRole] ur
                    ON u.UserID = ur.UserID
                   AND ou.EMAOrganizationID =
                       ur.EMAOrganizationID
                  WHERE ou.EMAOrganizationID = @OrgID",
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int userID = reader.GetInt32(0);
                if (!usersMap.TryGetValue(userID, out var user))
                {
                    user = new UserDto
                    {
                        UserID = userID,
                        UserName = reader.GetString(1),
                        EmailAddress = reader.GetString(2),
                        PhoneNumber = reader.GetString(3),
                        IsActive = reader.GetBoolean(4)
                    };
                    usersMap[userID] = user;
                }

                if (!reader.IsDBNull(5))
                {
                    user.RoleIDs.Add(reader.GetInt32(5));
                }
            }

            return new OkObjectResult(usersMap.Values.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching users for organization {OrgID}.", orgID);
            return new StatusCodeResult(500);
        }
    }

    public class UserDto
    {
        public int UserID { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public List<int> RoleIDs { get; set; } = new List<int>();
    }
}
