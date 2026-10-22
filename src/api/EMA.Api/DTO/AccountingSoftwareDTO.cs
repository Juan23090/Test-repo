namespace EMA.Api.DTOs;

public class AccountingSoftwareDTO
{
    public int AccountingSoftwareID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? MarketingURL { get; set; }
    public string? JESpecURL { get; set; }
}