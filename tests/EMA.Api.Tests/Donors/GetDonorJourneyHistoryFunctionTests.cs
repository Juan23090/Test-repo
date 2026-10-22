using EMA.Api.Functions.Donors;
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

namespace EMA.Api.Tests.Donors;

public sealed class GetDonorJourneyHistoryFunctionTests
{
    public GetDonorJourneyHistoryFunctionTests()
    {
        Environment.SetEnvironmentVariable("SqlConnectionString", "Server=localhost;Database=EMA;");
    }

    // Verifies function name and route mapping for donor journey history endpoint
    [Fact]
    public void GetDonorJourneyHistory_HasCorrectFunctionRouteAttribute()
    {
        var method = typeof(GetDonorJourneyHistory).GetMethod(nameof(GetDonorJourneyHistory.Run));
        Assert.NotNull(method);

        var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
        Assert.NotNull(functionAttr);
        Assert.Equal("GetDonorJourneyHistory", functionAttr.Name);

        var httpTriggerParam = method.GetParameters().FirstOrDefault(p => p.GetCustomAttribute<HttpTriggerAttribute>() != null);
        Assert.NotNull(httpTriggerParam);

        var httpTriggerAttr = httpTriggerParam.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(httpTriggerAttr);
        Assert.Equal("donors/journey-history/{orgID:int}/{donorID:int}", httpTriggerAttr.Route);
    }

    // Verifies constructor accepts logger dependency
    [Fact]
    public void GetDonorJourneyHistory_ConstructorInstantiatesWithLogger()
    {
        var mockLogger = new Mock<ILogger<GetDonorJourneyHistory>>();
        var functionInstance = new GetDonorJourneyHistory(mockLogger.Object);

        Assert.NotNull(functionInstance);
    }

    // Checks that invoking Run with invalid IDs returns an ObjectResult with a 500 status code
    [Fact]
    public async Task Run_WithInvalidIds_ReturnsObjectResultWith500StatusCode()
    {
        var mockLogger = new Mock<ILogger<GetDonorJourneyHistory>>();
        var functionInstance = new GetDonorJourneyHistory(mockLogger.Object);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await functionInstance.Run(request, orgID: -1, donorID: -1);

        Assert.NotNull(result);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }
}
