using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMA.App.Services;

public interface ITributeReportService
{
    Task<TributeReportResult> GetTributeReportAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<TributeDonorsResult> GetTributeDonorsAsync(
        int orgID,
        TributeDonorsFilter filter,
        CancellationToken cancellationToken = default);

    Task<TributeDonorsPdfResult> GetTributeDonorsPdfAsync(
        int orgID,
        TributeDonorsFilter filter,
        CancellationToken cancellationToken = default);

    Task<TributeReportActionResult> EmailTributeDonorsAsync(
        int orgID,
        TributeDonorsFilter filter,
        string recipientEmail,
        string subject,
        string message,
        CancellationToken cancellationToken = default);
}

public sealed class TributeReportResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public List<TributeReportItem>? Data { get; init; }
}

public sealed class TributeReportItem
{
    public int EMATributeTypeID { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    /// <summary>False for tributes recorded without a name (TributeText IS NULL).</summary>
    public bool HasName { get; set; }

    public int DonationCount { get; set; }
    public decimal TotalValue { get; set; }
}

/// <summary>
/// Identifies one Tribute Report row: a tribute type plus a name, or that type's unnamed
/// tributes when <see cref="HasName"/> is false.
/// </summary>
public sealed record TributeDonorsFilter(
    int TributeTypeID,
    bool HasName,
    string? Name,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed class TributeDonorsResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public TributeDonorsData? Data { get; init; }
}

public sealed class TributeDonorsData
{
    public string OrganizationName { get; set; } = string.Empty;
    public string TypeDescription { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool HasName { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<TributeDonorItem> Donors { get; set; } = new();
}

public sealed class TributeDonorItem
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
}

public sealed class TributeDonorsPdfResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public byte[]? FileBytes { get; init; }
    public string? FileName { get; init; }
}

public sealed class TributeReportActionResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}
