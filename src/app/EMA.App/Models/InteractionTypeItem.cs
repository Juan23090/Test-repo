namespace EMA.App.Models
{
    /// <summary>
    /// Represents an interaction type lookup entry for an organization.
    /// Backed by the OrganizationInteractionType table.
    /// </summary>
    public class InteractionTypeItem : AuditableEntity
    {
        public int EMAOrganizationID { get; set; }
        public int EMAInteractionTypeID { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
