namespace EMA.Api.Services;

public sealed class DonationIngestionResult
{
    public long NewDonationID { get; init; }

    public int EMADonorID { get; init; }

    public bool WasDonorCreated { get; init; }

    public bool WasDuplicate { get; init; }

    public int? EMAFundID { get; init; }

    public int? EMACampusID { get; init; }

    public int? EMAMethodOfGivingID { get; init; }

    public int? DonationStepID { get; init; }

    public bool WasSuccessful { get; init; }
}