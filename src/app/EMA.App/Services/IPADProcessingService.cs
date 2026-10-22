namespace EMA.App.Services;

public interface IPADProcessingService
{
    Task<PADProcessResult> ProcessPdfStatementAsync(Stream pdfStream, int organizationID, int userID, int? methodOfGivingID = null, string? bankId = null);
    Task<PADProcessResult> ProcessStatementFileAsync(Stream fileStream, string fileName, int organizationID, int userID, int? methodOfGivingID = null, string? bankId = null);
    Task<List<PADMatchResultDto>> MatchDonorsForBatchAsync(int organizationId, int batchId);
    Task<bool> UpdateBatchLineDonorAsync(int padBatchLineId, string officialDonorName, int padBatchId = 0);
    Task<List<PADBatchItemDto>> GetPADBatchesAsync(int organizationId);
    Task<List<BankItemDto>> GetBanksAsync();
    Task<PADCreateDonationsResultDto> CreateDonationRecordsForBatchAsync(int organizationId, int batchId, int userId, Dictionary<long, int>? lineDonorAssignments = null, List<int>? deletedLineIds = null, Dictionary<long, int>? lineFundOverrides = null);
}

public class PADCreateDonationsResultDto
{
    public bool Success { get; set; }
    public int PadBatchId { get; set; }
    public int DonationsCreated { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class PADProcessResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PADBatchID { get; set; }
    public int PADBatchStatusID { get; set; } = 30;
    public string StatusDescription { get; set; } = "PAD Details Imported";
    public int TotalExtractedLines { get; set; }
    public int FilteredDonationLinesCount { get; set; }
    public int TotalTransactionsRead { get; set; }
    public int ETransfersCount { get; set; }
    public int PadsCount { get; set; }
    public int UncertainMatchesCount { get; set; }
    public string DateRangeSummary { get; set; } = string.Empty;
    public List<string> ExtractedDonationLines { get; set; } = new();
}

public class PADMatchResultDto
{
    public int PADBatchLineID { get; set; }
    public string PADDonorName { get; set; } = "";
    public DateTime? Date { get; set; }
    public decimal DonationAmount { get; set; }
    public string TransactionType { get; set; } = "PAD";
    public string UnalteredText { get; set; } = "";
    public int? MatchedDonorID { get; set; }
    public string MatchedDonorName { get; set; } = "";
    public string MatchStatus { get; set; } = "No Match";
    public bool IsAutoMatched { get; set; }
    public bool IsAmbiguous { get; set; }
    public List<CandidateDonorUI> CandidateDonors { get; set; } = new();
}

public class CandidateDonorUI
{
    public int EMADonorID { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EnvelopeNumber { get; set; } = "";
    public int MatchScore { get; set; }
}

public class PADBatchItemDto
{
    public int PADBatchID { get; set; }
    public int PADBatchStatusID { get; set; }
    public string StatusDescription { get; set; } = "";
    public string BankName { get; set; } = "";
    public DateTime DateTimeCreated { get; set; }
    public string CreatedByUserName { get; set; } = "";
    public DateTime DateTimeUpdated { get; set; }
    public string UpdatedByUserName { get; set; } = "";
    public string TransactionDatesSummary { get; set; } = "";
}

public class BankKeyPhraseDto
{
    public string KeyPhrase { get; set; } = string.Empty;
    public string TransactionTypeID { get; set; } = "PAD";
}

public class BankItemDto
{
    public string BankID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? InstructionsURL { get; set; }
}
