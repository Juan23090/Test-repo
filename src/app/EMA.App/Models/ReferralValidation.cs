using System.Net.Mail;

namespace EMA.App.Models;

/// <summary>
/// Limits shared by the referral screens.
/// </summary>
/// <remarks>
/// These mirror the authoritative values in
/// EMA.Api.Services.Referrals.ReferralService. The API always re-checks; these
/// exist so the form never presents input as acceptable that the API rejects.
/// </remarks>
public static class ReferralLimits
{
    /// <summary>
    /// Keep in step with ReferralService.MaxRecipientsPerBatch.
    /// </summary>
    public const int MaxRecipientsPerBatch = 50;
}

/// <summary>
/// Referral email address validation.
/// </summary>
public static class ReferralEmail
{
    private const int MaxLength = 254;

    /// <summary>
    /// Applies the same parse the API uses (<see cref="MailAddress"/>), so a row
    /// marked valid in the browser is a row the API will accept.
    /// </summary>
    public static bool IsValidFormat(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > MaxLength)
        {
            return false;
        }

        try
        {
            var address = new MailAddress(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
