namespace EMA.App.Models
{
    /// <summary>
    /// Represents one canonical entity name and this organization's custom display
    /// name for it (if set). Backed by ChMSEntityAlias (canonical name list) left-joined
    /// with OrganizationEntityAliase (per-org override). Key-value override, not a
    /// flat add/remove list: no surrogate ID, no IsActive.
    /// </summary>
    public class EntityAliasItem
    {
        public string EntityName { get; set; } = string.Empty;
        public string? AliasName { get; set; }
    }
}
