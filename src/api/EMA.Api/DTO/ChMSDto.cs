namespace EMA.Api.DTOs;

// Mirrors the dbo.ChMS table (property names match column names exactly, incl. the
// "Backgound" misspelling and the lower-b audit columns — preserve as-is).
public class ChMSDTO
{
    public int ChMSID { get; set; }
    public string ChMSName { get; set; } = string.Empty;
    public string? Country { get; set; }
    public int? CanadianInstalls { get; set; }
    // Candidacy: char(30) FK columns that store the lookup text itself (RTRIM'd on read).
    public string? ChMSCandidateForEMAID { get; set; }        // FK ChMSCandidateForEMA
    public string? ChMSCandidateRatingID { get; set; }        // FK ChMSCandidateRating
    public string? ChMSAPIRating { get; set; }                // FK ChMSAPIRating
    public string? ChMSCandidateDecisionBackgound { get; set; } // renamed from EtranCandidateDecisionBackgound
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
}
