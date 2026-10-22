namespace EMA.App.Models
{
    /// <summary>
    /// Represents a method of giving lookup entry for an organization.
    /// </summary>
    public class MethodOfGivingItem : AuditableEntity
    {
        public int EMAOrganizationID { get; set; }
        public int EMAMethodOfGivingID { get; set; }
        public string? ChMSMethodOfGivingID { get; set; }
        public string Description { get; set; } = string.Empty;
        public int? InvoiceGroup { get; set; }
        public bool CRAReceiptable { get; set; }
        public bool? JournalEntryInDetail { get; set; }
        public bool? AcknowledgeToDonor { get; set; }
        public bool? AcknowledgeToOrganization { get; set; }
        public bool? AcknowledgeToEMASupport { get; set; }
        public string? AcknowledgementMessage { get; set; }
        public bool IsActive { get; set; }
        public bool IsDefault { get; set; }
    }
}
