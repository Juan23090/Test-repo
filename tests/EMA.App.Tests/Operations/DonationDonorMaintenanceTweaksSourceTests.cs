using Xunit;

namespace EMA.App.Tests.Operations;

public sealed class DonationDonorMaintenanceTweaksSourceTests
{
    [Fact]
    public void DonationsShowSavedRecordsNewestFirstWithoutReferenceOrMemoColumns()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        Assert.Contains("Donation {savedDonationID} saved successfully.", source);
        Assert.Contains("SearchMode =\n                \"Recent\";", source);
        Assert.Contains("OrderByDescending(donation => donation.DateTimeReceived)", source);
        Assert.Contains("ThenByDescending(donation => donation.EMADonationID)", source);
        Assert.DoesNotContain("SearchMode == \"Recent\"\n            ? AllDonations.OrderByDescending", source);
        Assert.DoesNotContain("@onclick='() => SortBy(\"Reference\")'", source);
        Assert.DoesNotContain("@onclick='() => SortBy(\"Memo\")'", source);
        Assert.DoesNotContain("class=\"td-memo\"", source);
    }

    [Fact]
    public void DonorValidationIsConditionalAndBelowSaveButtons()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donors.razor");

        var saveButtons = source.IndexOf("<div class=\"save-buttons\">", StringComparison.Ordinal);
        var validation = source.IndexOf("<div class=\"validation-message donor-validation-message\"", StringComparison.Ordinal);

        Assert.True(saveButtons >= 0);
        Assert.True(validation > saveButtons);
        Assert.DoesNotContain("<div class=\"form-note\">", source);
        Assert.Contains("role=\"alert\"", source);
    }

    [Fact]
    public void NewDonorUsesFirstActiveStatusBeforeSave()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donors.razor");

        Assert.Contains("CreateNewDonor();", source);
        Assert.Contains("status.Sequence ?? int.MaxValue", source);
        Assert.Contains("CurrentDonor.DonorStatusID =", source);
    }

    [Fact]
    public void ClearConfirmationIsOnlyShownForEnteredDataAndIsNotNativeBrowserConfirm()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donors.razor");

        Assert.Contains("if (!HasDonorDataToClear())", source);
        Assert.Contains("@if (ShowClearConfirm)", source);
        Assert.DoesNotContain("InvokeAsync<bool>(\"confirm\"", source);
    }

    [Fact]
    public void DonorAuditParsingAcceptsSerializerCasing()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donors.razor");

        Assert.Contains("TryGetPropertyIgnoreCase", source);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", source);
    }

    [Fact]
    public void EnterDonationsAuditAcceptsSerializerCasingAndUsesActingUser()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");
        var helper = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "DonationFunctionHelpers.cs");
        var endpoint = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "GetDonationAudit.cs");

        Assert.Contains("TryGetPropertyIgnoreCase", page);
        Assert.Contains("\"UpdatedByUserID\"", page);
        Assert.Contains("@UpdatedByUserID", helper);
        Assert.Contains("audit.UpdatedByUserID", endpoint);
        Assert.DoesNotContain("DonationAuditID", endpoint);
    }

    [Fact]
    public void StepLogIsAvailableForEverySavedDonation()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        Assert.Contains("disabled=\"@(CurrentDonation.EMADonationID == 0)\"", source);
        Assert.Contains("title=\"Steps taken with this donation\"", source);
        Assert.DoesNotContain("Step log is only available for e-transfer email donations.", source);
        Assert.DoesNotContain("private bool IsEmailDonation", source);
    }

    [Fact]
    public void DonationListSelectsEveryColumnRequiredByTheSharedMapper()
    {
        var source = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "GetDonations.cs");

        Assert.Contains("d.DonorToOrgChangeRequest", source);
        Assert.Contains("DonationFunctionHelpers.ReadDonation", source);
    }

    [Fact]
    public void DonationUpdatesLogRequiredSteps()
    {
        var helpers = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "DonationFunctionHelpers.cs");
        var update = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "UpdateDonation.cs");
        var get = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "GetDonationById.cs");

        var create = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "CreateDonation.cs");

        Assert.Contains("ManuallyKeyedDonationCreatedStepID = 500", helpers);
        Assert.Contains("ManuallyEditedDonationStepID = 520", helpers);
        Assert.Contains("OfficeNotificationLoadedStepID = 115", helpers);
        Assert.Contains("OfficeNotificationSavedStepID = 120", helpers);
        Assert.Contains("DonationFunctionHelpers.ManuallyKeyedDonationCreatedStepID", create);
        Assert.Contains("DonationFunctionHelpers.ManuallyEditedDonationStepID", update);
        Assert.Contains("DonationFunctionHelpers.OfficeNotificationSavedStepID", update);
        Assert.Contains("DonationFunctionHelpers.OfficeNotificationLoadedStepID", get);
        Assert.Contains("NOT EXISTS", helpers);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", helpers);
        Assert.Contains("SET DateTimeLogged = SYSUTCDATETIME()", helpers);
        Assert.Contains("@RefreshLoggedTime", helpers);
        Assert.Contains("DonationFunctionHelpers.ManuallyKeyedDonationCreatedStepID,\n                refreshLoggedTime: false", create);
        Assert.Contains("DonationFunctionHelpers.ManuallyEditedDonationStepID,\n                refreshLoggedTime: true", update);
        Assert.Contains("DonationFunctionHelpers.OfficeNotificationSavedStepID,\n                    refreshLoggedTime: false", update);
        Assert.Contains("DonationFunctionHelpers.OfficeNotificationLoadedStepID,\n                    refreshLoggedTime: false", get);
        Assert.DoesNotContain("logOnlyOnce", helpers);
    }

    [Fact]
    public void DonationCounterFieldsFollowTheMobileEntryOrder()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        var additionalToggle = source.IndexOf("Show Additional Fields", StringComparison.Ordinal);
        var date = source.IndexOf("<strong>Date:</strong>", StringComparison.Ordinal);
        var method = source.IndexOf("<strong>Method:</strong>", StringComparison.Ordinal);
        var eventField = source.IndexOf("<strong>Event:</strong>", StringComparison.Ordinal);
        var donationSource = source.IndexOf("<strong>Source:</strong>", StringComparison.Ordinal);
        var tribute = source.IndexOf("<strong>Tribute:</strong>", StringComparison.Ordinal);
        var anonymous = source.IndexOf("<strong>Anonymous:</strong>", StringComparison.Ordinal);
        var comments = source.IndexOf("<strong>Comments:</strong>", StringComparison.Ordinal);
        var donor = source.IndexOf("<strong>Donor:</strong>", StringComparison.Ordinal);
        var reference = source.IndexOf("<strong>Reference (optional):</strong>", StringComparison.Ordinal);
        var total = source.IndexOf("<strong>Total:</strong>", StringComparison.Ordinal);
        var fund = source.IndexOf("<strong><u>Fund</u></strong>", StringComparison.Ordinal);
        var campus = source.IndexOf("<strong><u>Campus</u></strong>", StringComparison.Ordinal);
        var save = source.IndexOf("Click Here to Save", StringComparison.Ordinal);
        var history = source.IndexOf("class=\"donation-history-row\"", StringComparison.Ordinal);

        Assert.True(additionalToggle >= 0);
        Assert.True(date > additionalToggle);
        Assert.True(method > date);
        Assert.True(eventField > method);
        Assert.True(donationSource > eventField);
        Assert.True(tribute > donationSource);
        Assert.True(comments > tribute);
        Assert.True(anonymous > comments);
        Assert.True(donor > anonymous);
        Assert.True(reference > donor);
        Assert.True(total > reference);
        Assert.True(fund > total);
        Assert.True(campus > fund);
        Assert.True(save > campus);
        Assert.True(history > save);
        Assert.Contains("class=\"icon-action", source);
        Assert.Contains("title=\"Search by date\"", source);
        Assert.Contains("title=\"Search by donor\"", source);
        Assert.Contains("title=\"Create new donor\"", source);
        Assert.Contains("size=\"15\"", source);
        Assert.Contains("maxlength=\"15\"", source);
    }

    [Fact]
    public void DonationDonorSearchIncludesAndDisplaysCity()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");
        var endpoint = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donors", "SearchDonors.cs");

        Assert.Contains("FormatDonorDisplay", page);
        Assert.Contains("donor.City", page);
        Assert.Contains("City, '') LIKE @Term", endpoint);
    }

    [Fact]
    public void DonationDonorSearchMatchesPartialTermsAcrossAllDonorFields()
    {
        var endpoint = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donors", "SearchDonors.cs");

        Assert.Contains("StringSplitOptions.RemoveEmptyEntries", endpoint);
        Assert.Contains("OfficialDonorName, '') LIKE @Term", endpoint);
        Assert.Contains("EmailAddress, '') LIKE @Term", endpoint);
        Assert.Contains("PhoneNumber, '') LIKE @Term", endpoint);
        Assert.Contains("Address, '') LIKE @Term", endpoint);
        Assert.Contains("City, '') LIKE @Term", endpoint);
        Assert.Contains("PostalCode, '') LIKE @Term", endpoint);
        Assert.Contains("EscapeLikePattern", endpoint);
    }

    [Fact]
    public void DonationStepLogLoadsNewestFirstWithOrganizationDateFormatting()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");
        var endpoint = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Reports", "GetDonationStepLog.cs");

        Assert.Contains("ORDER BY l.DateTimeLogged DESC", endpoint);
        Assert.Contains("Parameters.Add(\"@DonationID\", System.Data.SqlDbType.BigInt)", endpoint);
        Assert.Contains("StepsLoading = true", page);
        Assert.Contains("OrgDateFormat.Format(dateTime, OrganizationDateFormat)", page);
    }

    [Fact]
    public void DonationClearAndAnonymousControlsFollowConfirmedDesign()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        Assert.Contains("@if (ShowClearConfirm)", page);
        Assert.Contains("HasDonationDataToClear", page);
        Assert.Contains("Donations changed to anonymous are not reversible.", page);
        Assert.Contains("AnonymousDonorName", page);
        Assert.Contains("Donor name: {AnonymousDonorName}", page);
        Assert.Contains("class=\"form-input anon-message-box\"", page);
        Assert.DoesNotContain("textarea class=\"form-input form-textarea anon-message-box\"", page);
    }

    [Fact]
    public void DonationAnonymousControlUsesThePersistedOrganizationSwitch()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");
        var getSettings = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Customization", "GetOrgSettings.cs");
        var saveSettings = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Customization", "PostOrgSettings.cs");

        Assert.Contains("UseAnonymousEnabled", page);
        Assert.Contains("AS UseAnonymousEnabled", getSettings);
        Assert.Contains("UseAnonymous = @UseAnonymous", saveSettings);
        Assert.Contains("identity.EMAOrganizationID != orgID", saveSettings);
        Assert.DoesNotContain("sp_UpdateOrgUseFlags", saveSettings);
    }

    [Fact]
    public void DonationDateHeaderSortsRecentAndFilteredLists()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        Assert.Contains("@onclick='() => SortBy(\"Date\")'", page);
        Assert.Contains("private IEnumerable<DonationItem> SortedDonations =>\n        SortColumn switch", page);
        Assert.Contains("SortColumn =\n                \"Date\";", page);
        Assert.Contains("SortAsc =\n                false;", page);
    }

    [Fact]
    public void DonationTotalAndCampusDefaultsDriveDistributions()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");
        var settings = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Customization", "GetOrgSettings.cs");

        Assert.Contains("OnDonationTotalChanged", page);
        Assert.Contains("AllocateDonationTotal", page);
        Assert.Contains("CurrentDonation.DonationAmount - TotalAmount", page);
        Assert.Contains("OnMethodChanged", page);
        Assert.Contains("OnFundChanged", page);
        Assert.Contains("GetMethodCampusID", page);
        Assert.Contains("?.EMACampusID", page);
        Assert.Contains("EMACampusIDForEtransfers", settings);
        Assert.Contains("EMACampusIDForCreditDebitCards", settings);
        Assert.Contains("EMACampusIDForPADs", settings);
    }

    [Fact]
    public void MatchingRecentDonationsCanDefaultTheTotalAndSplit()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        Assert.Contains("ApplyMatchingRecentDonationAsync", source);
        Assert.Contains("Take(2)", source);
        Assert.Contains("HaveMatchingAllocations", source);
        Assert.Contains("last two donations had the same total and fund allocation", source);
    }

    [Fact]
    public void DonationReferenceIsOptionalAndDistributionRowsCanBeAdded()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");
        var helpers = ReadSource(
            "src", "api", "EMA.Api", "Functions", "Donations", "DonationFunctionHelpers.cs");
        var migration = ReadSource(
            "src", "db", "migrations", "20260921_OptionalDonationReference.sql");

        Assert.DoesNotContain("Please enter a reference.", page);
        Assert.DoesNotContain("A reference number is required.", helpers);
        Assert.Contains("Reference (optional):", page);
        Assert.Contains("string.IsNullOrWhiteSpace(donation.ReferenceNumber)", helpers);
        Assert.Contains("@onclick=\"AddDistributionRow\"", page);
        Assert.Contains("DistributionRows.Add(", page);
        Assert.Contains("+ Add Distribution", page);
        Assert.Contains("ALTER COLUMN ReferenceNumber nvarchar(255) NULL", migration);
        Assert.Contains("WHERE ReferenceNumber IS NOT NULL", migration);
    }

    [Fact]
    public void DonationListDateColumnIncludesTheUtcTime()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        Assert.Contains("@FormatDateTime(donation.DateTimeReceived)", page);
        Assert.Contains("{dateTime.Value:HH:mm:ss}", page);
    }

    [Fact]
    public void NewManualDonationsPersistTheCurrentUtcTime()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "Donations.razor");

        Assert.Contains("DateTimeReceived =\n                    DateTime.UtcNow", page);
        Assert.Contains("CurrentDonation.EMADonationID == 0", page);
        Assert.Contains("utcNow.TimeOfDay", page);
        Assert.Contains("DateTimeKind.Utc", page);
    }

    private static string ReadSource(params string[] segments)
    {
        var path = segments.Aggregate(FindRepositoryRoot(), Path.Combine);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "app", "EMA.App")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the EMA repository root.");
    }
}
