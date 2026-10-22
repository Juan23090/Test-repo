namespace EMA.App.Models
{
    /// <summary>
    /// Represents a fund lookup entry for an organization.
    /// </summary>
    public class FundItem : AuditableEntity
    {
        public int EMAOrganizationID { get; set; }
        public int EMAFundID { get; set; }
        public string? ChMSFundID { get; set; }
        public string FundName { get; set; } = string.Empty;
        public bool TaxDeductible { get; set; }
        public int? EMACampusID { get; set; }
        public bool FundActive { get; set; }
        public bool IsDefault { get; set; }
    }
}
