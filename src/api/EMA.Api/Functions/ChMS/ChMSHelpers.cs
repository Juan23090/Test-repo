using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Organization;

/// <summary>
/// Shared SQL-parameter binding for the ChMS write endpoints (PostChMS / PutChMS),
/// so char(1) 'Y'/'N' flags, money values, and NULLs are normalized in one place.
/// </summary>
internal static class ChMSHelpers
{
    /// <summary>Binds every editable ChMS column as a parameter (not the ID or audit columns).</summary>
    public static void AddEditableParameters(SqlCommand cmd, ChMSDTO dto)
    {
        cmd.Parameters.AddWithValue("@ChMSName", (object?)Trimmed(dto.ChMSName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Country", (object?)Trimmed(dto.Country) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CanadianInstalls", (object?)dto.CanadianInstalls ?? DBNull.Value);
        // Candidacy FKs store the lookup text itself (char(30)); trim, store as-is.
        cmd.Parameters.AddWithValue("@ChMSCandidateForEMAID", (object?)Trimmed(dto.ChMSCandidateForEMAID) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSCandidateRatingID", (object?)Trimmed(dto.ChMSCandidateRatingID) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSAPIRating", (object?)Trimmed(dto.ChMSAPIRating) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSCandidateDecisionBackgound", (object?)Trimmed(dto.ChMSCandidateDecisionBackgound) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MirrorTransactionEmails", (object?)Flag(dto.MirrorTransactionEmails) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSMonthlyRentDefault", (object?)dto.ChMSMonthlyRentDefault ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSPercentBasedFee", (object?)dto.ChMSPercentBasedFee ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSPerTransactionFee", (object?)dto.ChMSPerTransactionFee ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSURL", (object?)Trimmed(dto.ChMSURL) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSAPIDocURL", (object?)Trimmed(dto.ChMSAPIDocURL) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSOnboardingDocumentURL", (object?)Trimmed(dto.ChMSOnboardingDocumentURL) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSOnboardingVideoURL", (object?)Trimmed(dto.ChMSOnboardingVideoURL) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChMSDataPortURL", (object?)Trimmed(dto.ChMSDataPortURL) ?? DBNull.Value);
    }

    private static string? Trimmed(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    // char(1) 'Y'/'N' flag: anything starting with y/Y => "Y", else if provided => "N", else null.
    private static string? Flag(string? value)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.StartsWith("Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "N";
    }
}
