 namespace EMA.App.Models;
 using System.Text.Json.Serialization;
public class OrganizationAuditObject
{
    [JsonPropertyName("OrganizationAuditPayload")]
    public OrganizationSummary Payload { get; set; } = new();

    [JsonPropertyName("DateTimeCreated")]
    public DateTime Timestamp { get; set; }
    public OrganizationAuditObject Clone()
    {
        return (OrganizationAuditObject)this.MemberwiseClone();
    }
}