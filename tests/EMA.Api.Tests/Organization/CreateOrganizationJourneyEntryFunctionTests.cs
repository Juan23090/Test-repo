using EMA.Api.Functions.Organization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace EMA.Api.Tests.Organization;

public sealed class CreateOrganizationJourneyEntryFunctionTests
{
    public CreateOrganizationJourneyEntryFunctionTests()
    {
        Environment.SetEnvironmentVariable("SqlConnectionString", "Server=localhost;Database=EMA;");
    }

    // Verifies function name and route mapping for creating organization journey entry endpoint
    [Fact]
    public void CreateOrganizationJourneyEntry_HasCorrectFunctionRouteAttribute()
    {
        var method = typeof(CreateOrganizationJourneyEntry).GetMethod(nameof(CreateOrganizationJourneyEntry.Run));
        Assert.NotNull(method);

        var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
        Assert.NotNull(functionAttr);
        Assert.Equal("CreateOrganizationJourneyEntry", functionAttr.Name);

        var httpTriggerParam = method.GetParameters().FirstOrDefault(p => p.GetCustomAttribute<HttpTriggerAttribute>() != null);
        Assert.NotNull(httpTriggerParam);

        var httpTriggerAttr = httpTriggerParam.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(httpTriggerAttr);
        Assert.Equal("organization/journey-history/{orgID:int}", httpTriggerAttr.Route);
    }

    // Verifies constructor accepts logger dependency
    [Fact]
    public void CreateOrganizationJourneyEntry_ConstructorInstantiatesWithLogger()
    {
        var mockLogger = new Mock<ILogger<CreateOrganizationJourneyEntry>>();
        var functionInstance = new CreateOrganizationJourneyEntry(mockLogger.Object);

        Assert.NotNull(functionInstance);
    }

    // Checks that posting journey entry JSON payload with invalid connection returns ObjectResult 500
    [Fact]
    public async Task Run_WithJourneyPayload_HandlesDatabaseErrorsGracefully()
    {
        var mockLogger = new Mock<ILogger<CreateOrganizationJourneyEntry>>();
        var functionInstance = new CreateOrganizationJourneyEntry(mockLogger.Object);

        var httpContext = new DefaultHttpContext();
        var jsonPayload = "{\"UserId\":1, \"ActivityTypeID\":2, \"InteractionComments\":\"Test follow up entry\"}";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonPayload));

        var result = await functionInstance.Run(httpContext.Request, orgID: 18);

        Assert.NotNull(result);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    // Checks that posting malformed JSON payload catches deserialization errors and returns 500 ObjectResult
    [Fact]
    public async Task Run_WithMalformedJsonPayload_ReturnsErrorObjectResult()
    {
        var mockLogger = new Mock<ILogger<CreateOrganizationJourneyEntry>>();
        var functionInstance = new CreateOrganizationJourneyEntry(mockLogger.Object);

        var httpContext = new DefaultHttpContext();
        var malformedPayload = "{ malformed_json_without_quotes: 123 ";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(malformedPayload));

        var result = await functionInstance.Run(httpContext.Request, orgID: 18);

        Assert.NotNull(result);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }
}
