namespace EMA.Api.Services.Security;

public static class ApiRuntimeEnvironment
{
    public static bool IsDevelopment =>
        string.Equals(
            Environment.GetEnvironmentVariable(
                "AZURE_FUNCTIONS_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);

    // Unknown or misspelled environments must keep production controls on.
    public static bool IsProductionLike =>
        !IsDevelopment;
}
