namespace EMA.App.Models
{
    /// <summary>
    /// Represents a donor-status lookup entry for an organization.
    /// Backed by the DonorStatus table. Note: unlike the other lookups, IsActive is a
    /// real bit (not CHAR(1) Y/N), DonorStatusID is a non-identity smallint, and the
    /// table has no audit columns — so this is a plain model (no AuditableEntity).
    /// </summary>
    public class DonorStatusItem
    {
        public int EMAOrganizationID { get; set; }
        public int DonorStatusID { get; set; }
        public int? Sequence { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
