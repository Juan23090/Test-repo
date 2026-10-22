using System.Text.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public interface IDonationService
{
    Task<List<DonationItem>> GetAllAsync(
        int orgID,
        CancellationToken cancellationToken = default);

    Task<List<DonationItem>> GetByDonorAsync(
        int orgID,
        long donorID,
        CancellationToken cancellationToken = default);

    Task<List<DonationItem>> GetByDateAsync(
        int orgID,
        DateTime receivedDate,
        CancellationToken cancellationToken = default);

    Task<DonationItem?> GetByIDAsync(
        int orgID,
        long donationID,
        CancellationToken cancellationToken = default);

    Task<long?> CreateAsync(
        int orgID,
        DonationItem donation,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        int orgID,
        DonationItem donation,
        CancellationToken cancellationToken = default);

    Task ConfirmAsync(
        long donationID,
        CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(
        long donationID,
        CancellationToken cancellationToken = default);

    Task EmailSaveAsync(
        long donationID,
        object payload,
        CancellationToken cancellationToken = default);

    Task<List<JsonElement>> GetDonationStepLogAsync(
        long donationID,
        CancellationToken cancellationToken = default);

    Task<List<JsonElement>> GetDonationAuditAsync(
        int orgID,
        long donationID,
        CancellationToken cancellationToken = default);
}