namespace EMA.App.Models;

public class UserItem
{
    public int UserID { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public List<int> RoleIDs { get; set; } = new List<int>();

    public int RoleCount => RoleIDs.Count;
}
