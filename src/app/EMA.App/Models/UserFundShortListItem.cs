namespace EMA.App.Models;

/// <summary>
/// One row in a user's fund shortlist: an organization fund plus whether this user has picked it
/// for their Donations fund dropdown, and the position they want it in.
/// </summary>
public class UserFundShortListItem
{
    public int EMAFundID { get; set; }

    public string FundName { get; set; } = string.Empty;

    public int? Sequence { get; set; }

    public bool Selected { get; set; }
}
