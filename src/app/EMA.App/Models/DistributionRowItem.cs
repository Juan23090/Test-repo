namespace EMA.App.Models;

public class DistributionRowItem
{
    public long EMADonationID { get; set; }
    public long EMADonationDistributionID { get; set; }
    public int EMACampusID { get; set; }
    public int EMAFundID { get; set; }
    public decimal DistributionAmount { get; set; }
}
