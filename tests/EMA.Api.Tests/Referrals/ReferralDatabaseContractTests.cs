using System.Text.RegularExpressions;
using Xunit;

namespace EMA.Api.Tests.Referrals;

/// <summary>
/// Guards the boundary between ReferralService and the referral SQL.
/// </summary>
/// <remarks>
/// Every defect these tests cover shipped once already: the service read column
/// names that no stored procedure produced, so each referral endpoint threw at
/// runtime while the unit tests stayed green. Serialization round-trips cannot
/// catch that -- only comparing the two sides can.
/// </remarks>
public sealed class ReferralDatabaseContractTests
{
    private const string StoredProcedureFile = "20260901_ReferralStoredProcedures.sql";
    private const string SchemaFile = "InstallReferralProgramSchema.sql";

    [Fact]
    public void GetReferralHistoryReadsOnlyColumnsTheStoredProcedureReturns()
    {
        var readColumns = ReaderColumnsIn(
            MethodBody(ReadReferralServiceSource(), "GetReferralHistoryAsync"));

        var returnedColumns = SelectAliasesOf("sp_Referral_GetHistory");

        Assert.NotEmpty(readColumns);

        var missing = readColumns
            .Where(column => !returnedColumns.Contains(column))
            .OrderBy(column => column, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"ReferralService.GetReferralHistoryAsync reads column(s) that dbo.sp_Referral_GetHistory does not return: "
            + $"{string.Join(", ", missing)}. Returned columns: {string.Join(", ", returnedColumns.OrderBy(c => c, StringComparer.Ordinal))}.");
    }

    [Fact]
    public void AcceptReferralReadsOnlyColumnsTheStoredProcedureReturns()
    {
        var readColumns = ReaderColumnsIn(
            MethodBody(ReadReferralServiceSource(), "AcceptReferralAsync"));

        var returnedColumns = SelectAliasesOf("sp_Referral_Accept");

        Assert.NotEmpty(readColumns);
        Assert.All(
            readColumns,
            column => Assert.True(
                returnedColumns.Contains(column),
                $"dbo.sp_Referral_Accept does not return '{column}'."));
    }

    [Fact]
    public void GetEligibilityReadsOnlyColumnsTheStoredProcedureReturns()
    {
        var body = MethodBody(ReadReferralServiceSource(), "GetEligibilityAsync");
        var returnedColumns = SelectAliasesOf("sp_Referral_CheckEligibility");

        // The method also reads the SystemSettings template columns, which are
        // covered separately; only the procedure's own columns are checked here.
        foreach (var column in new[] { "IsEligible", "Reason", "DaysActive", "DonationCount" })
        {
            Assert.Contains($"reader[\"{column}\"]", body, StringComparison.Ordinal);
            Assert.Contains(column, returnedColumns);
        }
    }

    [Fact]
    public void SystemSettingsTemplateColumnsExistInTheSchemaMigration()
    {
        var body = MethodBody(ReadReferralServiceSource(), "GetEligibilityAsync");
        var schema = ReadDatabaseSource(SchemaFile);

        var templateColumns = ReaderColumnsIn(body)
            .Where(column => column.StartsWith("ReferralEmail", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(templateColumns);

        foreach (var column in templateColumns)
        {
            Assert.Contains($"ALTER TABLE dbo.SystemSettings ADD {column}", schema, StringComparison.Ordinal);
        }

        // The read must come from the newest settings row, matching how the rest
        // of the API reads dbo.SystemSettings.
        Assert.Contains("SELECT TOP (1)", body, StringComparison.Ordinal);
        Assert.Contains("ORDER BY SettingID DESC", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferralStoredProceduresAreDefinedExactlyOnce()
    {
        var databaseDirectory = Path.Combine(FindRepositoryRoot(), "src", "db");

        foreach (var procedure in new[] { "sp_Referral_CheckEligibility", "sp_Referral_Accept", "sp_Referral_GetHistory" })
        {
            var definingFiles = Directory
                .EnumerateFiles(databaseDirectory, "*.sql", SearchOption.AllDirectories)
                .Where(file => Regex.IsMatch(
                    File.ReadAllText(file),
                    $@"CREATE\s+(OR\s+ALTER\s+)?PROCEDURE\s+dbo\.{procedure}\b",
                    RegexOptions.IgnoreCase))
                .Select(Path.GetFileName)
                .ToList();

            Assert.True(
                definingFiles.Count == 1,
                $"dbo.{procedure} must have exactly one definition, found {definingFiles.Count}: {string.Join(", ", definingFiles)}.");

            Assert.Equal(StoredProcedureFile, definingFiles[0]);
        }
    }

    [Fact]
    public void StatusesWrittenByTheServiceAreClaimableOrTerminalInAcceptProcedure()
    {
        var service = ReadReferralServiceSource();
        var accept = ProcedureBody("sp_Referral_Accept");

        // The service parks a row in Queued before dispatch and promotes it to
        // Sent afterwards. Only Sent invitations can be accepted; Queued invitations
        // are blocked until successfully delivered.
        Assert.Contains("'Queued',", service, StringComparison.Ordinal);
        Assert.Contains("\"Sent\"", service, StringComparison.Ordinal);
        Assert.Contains("@CurrentStatus <> 'Sent'", accept, StringComparison.Ordinal);
        Assert.Contains("Queued", accept, StringComparison.Ordinal);

        // A failed dispatch must not leave a claimable invitation behind.
        Assert.Contains("\"Failed\"", service, StringComparison.Ordinal);
        Assert.DoesNotContain("'Failed'", accept, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferralCodeIsAUniqueBearerSecretRequiredByAcceptance()
    {
        var service = ReadReferralServiceSource();
        var accept = ProcedureBody("sp_Referral_Accept");
        var schema = ReadDatabaseSource(SchemaFile);

        Assert.Contains("new byte[32]", service, StringComparison.Ordinal);
        Assert.Contains("@ReferralCode VARCHAR(64)", accept, StringComparison.Ordinal);
        Assert.Contains("WHERE r.ReferralCode = @NormalizedCode", accept, StringComparison.Ordinal);
        Assert.Contains("@CurrentStatus <> 'Sent'", accept, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX IX_Referral_ReferralCode", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferralRoleMigrationSeedsOnlyOrganizationAdministrator()
    {
        var migration = ReadDatabaseSource("20260901_AddReferralRolePermissions.sql");

        Assert.Contains("WHERE r.RoleID IN (2000, 2500) -- Organization Administrator roles", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("RoleName LIKE", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE rmi", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferralMenuItemMigrationRejectsNullAndDifferentUrlCollisions()
    {
        var migration = ReadDatabaseSource("20260901_AddOrganizationReferralMenuItems.sql");

        Assert.Contains("WHERE existing.URL IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("OR existing.URL <> source.URL", migration, StringComparison.Ordinal);
        Assert.Contains("THROW 51410", migration, StringComparison.Ordinal);
        Assert.Contains("THROW 51411", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SendBatchCapIsEnforcedAndMirroredByTheClient()
    {
        var service = ReadReferralServiceSource();
        Assert.Contains("public const int MaxRecipientsPerBatch = 50;", service, StringComparison.Ordinal);
        Assert.Contains("request.Recipients.Count > MaxRecipientsPerBatch", service, StringComparison.Ordinal);

        var appLimits = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "app", "EMA.App", "Models", "ReferralValidation.cs"));

        Assert.Contains("public const int MaxRecipientsPerBatch = 50;", appLimits, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferralFunctionsTrustTheAuthenticatedIdentityRatherThanTheRoute()
    {
        foreach (var function in new[] { "SendReferralEmails.cs", "AcceptReferral.cs" })
        {
            var source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "src", "api", "EMA.Api", "Functions", "Referrals", function));

            Assert.Contains("req.GetRequiredApiIdentity()", source, StringComparison.Ordinal);

            // Falling back to the route orgID when the identity is missing would
            // persist writes attributed to user 0 on an unverified organization.
            Assert.DoesNotContain("effectiveOrgID = orgID;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("int effectiveUserID = 0;", source, StringComparison.Ordinal);
        }
    }

    private static SortedSet<string> ReaderColumnsIn(string source)
    {
        var columns = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(source, @"reader\[""(?<name>[^""]+)""\]"))
        {
            columns.Add(match.Groups["name"].Value);
        }

        return columns;
    }

    /// <summary>
    /// Collects the output column names of a procedure's final SELECT list --
    /// the alias after AS when present, otherwise the trailing identifier.
    /// </summary>
    private static HashSet<string> SelectAliasesOf(string procedureName)
    {
        var body = ProcedureBody(procedureName);
        var aliases = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(body, @"\bAS\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*(,|;|$)", RegexOptions.Multiline))
        {
            aliases.Add(match.Groups["alias"].Value);
        }

        // Columns projected without an alias, e.g. "r.CreditAmount," or "r.Status".
        foreach (Match match in Regex.Matches(body, @"^\s*[A-Za-z_][A-Za-z0-9_]*\.(?<column>[A-Za-z_][A-Za-z0-9_]*)\s*(,|$)", RegexOptions.Multiline))
        {
            aliases.Add(match.Groups["column"].Value);
        }

        return aliases;
    }

    private static string ProcedureBody(string procedureName)
    {
        var sql = ReadDatabaseSource(StoredProcedureFile);

        var start = sql.IndexOf($"PROCEDURE dbo.{procedureName}", StringComparison.OrdinalIgnoreCase);
        Assert.True(start >= 0, $"dbo.{procedureName} was not found in {StoredProcedureFile}.");

        var end = sql.IndexOf("\nGO", start, StringComparison.Ordinal);
        return end < 0 ? sql[start..] : sql[start..end];
    }

    private static string MethodBody(string source, string methodName)
    {
        var start = source.IndexOf($"{methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{methodName} was not found in ReferralService.");

        // Each public method is followed by the next member declaration; reading
        // to the next "    public " / "    private " keeps the body self-contained.
        var next = Regex.Match(source[start..], @"\n    (public|private|internal)\s");
        return next.Success ? source.Substring(start, next.Index) : source[start..];
    }

    private static string ReadReferralServiceSource() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "src", "api", "EMA.Api", "Services", "Referrals", "ReferralService.cs"));

    private static string ReadDatabaseSource(string fileName) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "src", "db", fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "api", "EMA.Api")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
