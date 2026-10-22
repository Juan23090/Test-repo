namespace EMA.App.Models;

// Mirrors the dbo.ChMS table. Used for both the grid list and the edit form on /chms.
// Property names match column names exactly (incl. the "Backgound" misspelling).
public class ChMSSummary
{
    public int ChMSID { get; set; }
    public string ChMSName { get; set; } = string.Empty;
    public string? Country { get; set; }
    public int? CanadianInstalls { get; set; }
    // Candidacy: char(30) FK columns whose value IS the lookup text (bound directly to dropdowns).
    public string? ChMSCandidateForEMAID { get; set; }
    public string? ChMSCandidateRatingID { get; set; }
    public string? ChMSAPIRating { get; set; }
    public string? ChMSCandidateDecisionBackgound { get; set; }
    public string? MirrorTransactionEmails { get; set; }      // char(1) 'Y'/'N'
    public decimal? ChMSMonthlyRentDefault { get; set; }      // money
    public decimal? ChMSPercentBasedFee { get; set; }         // money
    public decimal? ChMSPerTransactionFee { get; set; }       // money
    public string? ChMSURL { get; set; }
    public string? ChMSAPIDocURL { get; set; }
    public string? ChMSOnboardingDocumentURL { get; set; }
    public string? ChMSOnboardingVideoURL { get; set; }
    public string? ChMSDataPortURL { get; set; }

    // Audit (read-only; server-managed).
    public DateTime? DateTimeCreated { get; set; }
    public DateTime? DateTimeUpdated { get; set; }

    // Shallow copy for edit-buffer / Cancel-revert on the /chms page.
    public ChMSSummary Clone() => (ChMSSummary)MemberwiseClone();
}

// Dropdown option lists for the ChMS edit form (GET /api/chms/lookups). Each value is the
// literal lookup text, bound directly to the matching char(30) FK field on ChMSSummary.
public class ChMSLookups
{
    public List<string> CandidateFor { get; set; } = new();
    public List<string> CandidateRating { get; set; } = new();
    public List<string> ApiRating { get; set; } = new();
}
