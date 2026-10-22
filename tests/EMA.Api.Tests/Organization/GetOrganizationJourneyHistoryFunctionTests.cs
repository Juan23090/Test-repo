using EMA.Api.Functions.Organization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace EMA.Api.Tests.Organization;

public sealed class GetOrganizationJourneyHistoryFunctionTests
{
    public GetOrganizationJourneyHistoryFunctionTests()
    {
        Environment.SetEnvironmentVariable("SqlConnectionString", "Server=localhost;Database=EMA;");
    }

    // Verifies function name and route mapping for organization journey history endpoint
    [Fact]
    public void GetOrganizationJourneyHistory_HasCorrectFunctionRouteAttribute()
    {
        var method = typeof(GetOrganizationJourneyHistory).GetMethod(nameof(GetOrganizationJourneyHistory.Run));
        Assert.NotNull(method);

        var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
        Assert.NotNull(functionAttr);
        Assert.Equal("GetOrganizationJourneyHistory", functionAttr.Name);

        var httpTriggerParam = method.GetParameters().FirstOrDefault(p => p.GetCustomAttribute<HttpTriggerAttribute>() != null);
        Assert.NotNull(httpTriggerParam);

        var httpTriggerAttr = httpTriggerParam.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(httpTriggerAttr);
        Assert.Equal("organization/journey-history/{orgID:int}", httpTriggerAttr.Route);
    }

    // Verifies constructor accepts logger dependency
    [Fact]
    public void GetOrganizationJourneyHistory_ConstructorInstantiatesWithLogger()
    {
        var mockLogger = new Mock<ILogger<GetOrganizationJourneyHistory>>();
        var functionInstance = new GetOrganizationJourneyHistory(mockLogger.Object);

        Assert.NotNull(functionInstance);
    }

    // Checks that invoking Run with invalid org ID returns an ObjectResult with a 500 status code
    [Fact]
    public async Task Run_WithInvalidOrgId_ReturnsObjectResultWith500StatusCode()
    {
        var mockLogger = new Mock<ILogger<GetOrganizationJourneyHistory>>();
        var functionInstance = new GetOrganizationJourneyHistory(mockLogger.Object);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await functionInstance.Run(request, orgID: -1);

        Assert.NotNull(result);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }
}
