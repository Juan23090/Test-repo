using System.Reflection;
using EMA.Api.Functions.Banks;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Xunit;

namespace EMA.Api.Tests.Banks;

public sealed class BankEndpointSourceContractTests
{
    private static readonly Type[] BankFunctionClasses =
    {
        typeof(GetBankLookups),
        typeof(SaveBank),
        typeof(SaveBankTransactionType),
        typeof(SaveBankKeyPhrase)
    };

    private static readonly string[] ExpectedFunctionNames =
    {
        "GetBankLookups",
        "PostBank",
        "PutBank",
        "PostBankTransactionType",
        "PutBankTransactionType",
        "PostBankKeyPhrase",
        "PutBankKeyPhrase"
    };

    // make sure all the banks functions use anonymous binding so our central security middleware handles authorization
    [Fact]
    public void AllBankFunctionsUseAnonymousBinding()
    {
        foreach (var functionClass in BankFunctionClasses)
        {
            var methods = functionClass.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
                if (functionAttr is null)
                {
                    continue;
                }

                var httpTrigger = method.GetParameters()
                    .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                    .FirstOrDefault(a => a is not null);

                Assert.NotNull(httpTrigger);
                Assert.Equal(AuthorizationLevel.Anonymous, httpTrigger!.AuthLevel);
            }
        }
    }

    // make sure every single banks endpoint is registered in the policy registry as platform admin
    [Theory]
    [InlineData("GetBankLookups")]
    [InlineData("PostBank")]
    [InlineData("PutBank")]
    [InlineData("PostBankTransactionType")]
    [InlineData("PutBankTransactionType")]
    [InlineData("PostBankKeyPhrase")]
    [InlineData("PutBankKeyPhrase")]
    public void EveryBankEndpointIsRegisteredInPolicyRegistryAsPlatformAdmin(string functionName)
    {
        var registry = new ApiEndpointPolicyRegistry();
        var policy = registry.GetPolicy(functionName);

        Assert.Equal(ApiEndpointAccess.PlatformAdministrator, policy.Access);
    }

    // verify sql queries are parameterized so we dont have any sql injection vulnerabilities
    [Theory]
    [InlineData("SaveBank.cs")]
    [InlineData("SaveBankTransactionType.cs")]
    [InlineData("SaveBankKeyPhrase.cs")]
    [InlineData("GetBankLookups.cs")]
    public void BankFunctionsUseParameterizedSqlQueries(string fileName)
    {
        var filePath = FindRepositoryFile("src", "api", "EMA.Api", "Functions", "Banks", fileName);
        var fileContent = File.ReadAllText(filePath);

        Assert.Contains("SqlCommand", fileContent);
        Assert.DoesNotContain("$\"SELECT", fileContent);
        Assert.DoesNotContain("$\"INSERT", fileContent);
        Assert.DoesNotContain("$\"UPDATE", fileContent);
    }

    // make sure catch blocks log the error and don't leak raw exception messages back to clients
    [Theory]
    [InlineData("SaveBank.cs")]
    [InlineData("SaveBankTransactionType.cs")]
    [InlineData("SaveBankKeyPhrase.cs")]
    [InlineData("GetBankLookups.cs")]
    public void BankFunctionsMaskInternalExceptions(string fileName)
    {
        var filePath = FindRepositoryFile("src", "api", "EMA.Api", "Functions", "Banks", fileName);
        var fileContent = File.ReadAllText(filePath);

        Assert.Contains("_logger.LogError", fileContent);
        Assert.DoesNotContain("ex.Message", fileContent);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }
}
