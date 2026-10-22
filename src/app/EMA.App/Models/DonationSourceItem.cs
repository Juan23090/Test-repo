namespace EMA.App.Models
{
    /// <summary>
    /// Represents a donation source lookup entry for an organization.
    /// Backed by the OrganizationDonationSource table.
    /// </summary>
    public class DonationSourceItem : AuditableEntity
    {
        public int EMAOrganizationID { get; set; }
        public int EMADonationSourceID { get; set; }
        public int? ChMSDonationSource { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
