using System.Threading.Tasks;
using System.Collections.Generic;
using EMA.App.Models;

namespace EMA.App.Services;

public interface IOrganizationJourneyService
{
    Task<List<OrganizationJourneyEntry>> GetJourneyHistoryAsync(int orgID);
    Task<OrganizationJourneyFeedbackSummary> GetJourneyFeedbackAsync(int orgID);
    Task<int?> CreateJourneyEntryAsync(int orgID, OrganizationJourneyEntry entry);
    Task<bool> UpdateOrganizationDetailsAsync(int orgID, OrganizationSummary orgDetails);
    Task<List<OrganizationJourneyActivityType>> GetJourneyActivityTypesAsync();
    Task<List<OrganizationFollowUpItem>> GetFollowUpQueueAsync();
    Task<List<OrganizationStatusItem>> GetOrganizationStatusesAsync();
    Task<List<OrganizationSummary>> GetAllOrganizationsAsync();
    Task<List<ChMSOption>> GetChMSListAsync();
    Task<List<AccountingSoftwareSummary>> GetAccountingSoftwareAsync();
}
