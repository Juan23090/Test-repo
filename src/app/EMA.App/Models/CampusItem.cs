namespace EMA.App.Models
{
    /// <summary>
    /// Represents a campus lookup entry for an organization.
    /// </summary>
    public class CampusItem : AuditableEntity
    {
        public int EMAOrganizationID { get; set; }
        public int EMACampusID { get; set; }
        public string? ChMSCampusID { get; set; }
        public string CampusName { get; set; } = string.Empty;
        public int? DefaultCampusFundID { get; set; }
        public bool CampusActive { get; set; }
        public bool IsDefault { get; set; }
    }
}
