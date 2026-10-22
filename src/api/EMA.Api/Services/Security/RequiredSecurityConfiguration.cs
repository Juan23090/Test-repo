namespace EMA.Api.Services.Security;

public static class RequiredSecurityConfiguration
{
    public static int GetPositiveInt(
        string environmentVariableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            environmentVariableName);

        var rawValue =
            Environment.GetEnvironmentVariable(
                environmentVariableName);

        if (!int.TryParse(
                rawValue,
                out var value) ||
            value <= 0)
        {
            throw new InvalidOperationException(
                $"Missing or invalid positive integer environment variable '{environmentVariableName}'.");
        }

        return value;
    }
}
