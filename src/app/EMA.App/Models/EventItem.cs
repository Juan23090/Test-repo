namespace EMA.App.Models
{
    /// <summary>
    /// Represents an organization event lookup entry (formerly "Currency Type").
    /// Backed by the OrganizationEvent table.
    /// </summary>
    public class EventItem : AuditableEntity
    {
        public int EMAOrganizationID { get; set; }
        public int EMAEventID { get; set; }
        public string? ChMSEventID { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
