namespace EMA.App.Models;

public sealed class UserSession
{
    public int UserID { get; init; }

    public int? DonorID { get; init; }

    public bool IsDonorSession { get; init; }

    public string UserName { get; init; } =
        string.Empty;

    public string EmailAddress { get; init; } =
        string.Empty;

    public string PhoneNumber { get; init; } =
        string.Empty;

    public int EMAOrganizationID { get; init; }

    public string OrganizationName { get; init; } =
        string.Empty;

    public List<LoginRole> Roles { get; init; } =
        new();

    public List<int> AllowedCategoryIDs { get; init; } =
        new();

    public int CurrentCategoryID { get; set; }

    public string AccessToken { get; init; } =
        string.Empty;

    public DateTimeOffset?
        AccessTokenExpiresAtUtc
    {
        get;
        init;
    }

    public int ActorID =>
        IsDonorSession
            ? DonorID ?? UserID
            : UserID;
}