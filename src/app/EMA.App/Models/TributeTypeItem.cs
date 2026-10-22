namespace EMA.App.Models
{
    /// <summary>
    /// Represents a tribute type lookup entry for an organization.
    /// Backed by the OrganizationTributeType table.
    /// </summary>
    public class TributeTypeItem : AuditableEntity
    {
        public int EMAOrganizationID { get; set; }
        public int EMATributeTypeID { get; set; }
        public string? ChMSTributeTypeID { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
