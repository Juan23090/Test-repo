namespace EMA.App.Services;

public interface IJournalEntryReportService
{
    Task<JournalEntryReportResult> GetJournalEntryDetailsAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string sortBy,
        bool jeByDate,
        CancellationToken cancellationToken = default);

    Task<JournalEntryPdfResult> GetJournalEntryPdfAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string sortBy,
        bool jeByDate,
        CancellationToken cancellationToken = default);

    Task<JournalEntryCsvResult> GetJournalEntryCsvAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string sortBy,
        bool jeByDate,
        CancellationToken cancellationToken = default);
}

public sealed class JournalEntryReportResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public JournalEntryReportResponse? Data { get; init; }
}

public sealed class JournalEntryPdfResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public byte[]? FileBytes { get; init; }
    public string? FileName { get; init; }
}

public sealed class JournalEntryCsvResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public byte[]? FileBytes { get; init; }
    public string? FileName { get; init; }
}

public sealed class JournalEntryReportResponse
{
    public int EMAOrganizationID { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string SortBy { get; set; } = string.Empty;
    public bool JeByDate { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public List<JournalEntryGroup> Journals { get; set; } = new();
}

public sealed class JournalEntryGroup
{
    public string JournalDate { get; set; } = string.Empty;
    public string JournalNo { get; set; } = string.Empty;
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public List<JournalEntryItem> Entries { get; set; } = new();
}

public sealed class JournalEntryItem
{
    public string Account { get; set; } = string.Empty;
    public decimal? Debits { get; set; }
    public decimal? Credits { get; set; }
    public string Description { get; set; } = string.Empty;
}
