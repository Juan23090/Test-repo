using EMA_Api.Functions.Operations;
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

namespace EMA.Api.Tests.Operations;

public sealed class GetSavingsCalculatorDataFunctionTests
{
    public GetSavingsCalculatorDataFunctionTests()
    {
        Environment.SetEnvironmentVariable("SqlConnectionString", "Server=localhost;Database=EMA;");
    }

    // Verifies function attribute and route mapping for savings calculator data endpoint
    [Fact]
    public void GetSavingsCalculatorData_HasCorrectFunctionRouteAttribute()
    {
        var method = typeof(GetSavingsCalculatorData).GetMethod(nameof(GetSavingsCalculatorData.Run));
        Assert.NotNull(method);

        var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
        Assert.NotNull(functionAttr);
        Assert.Equal("GetSavingsCalculatorData", functionAttr.Name);

        var httpTriggerParam = method.GetParameters().FirstOrDefault(p => p.GetCustomAttribute<HttpTriggerAttribute>() != null);
        Assert.NotNull(httpTriggerParam);

        var httpTriggerAttr = httpTriggerParam.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(httpTriggerAttr);
        Assert.Equal("savings-calculator/{orgId:int}", httpTriggerAttr.Route);
    }

    // Verifies constructor correctly accepts logger dependency
    [Fact]
    public void GetSavingsCalculatorData_ConstructorInstantiatesWithLogger()
    {
        var mockLogger = new Mock<ILogger<GetSavingsCalculatorData>>();
        var functionInstance = new GetSavingsCalculatorData(mockLogger.Object);

        Assert.NotNull(functionInstance);
    }

    // Checks that invoking Run with invalid org ID returns a 500 StatusCodeResult or NotFoundObjectResult
    [Fact]
    public async Task Run_WithInvalidOrgId_ReturnsErrorOrNotFoundResult()
    {
        var mockLogger = new Mock<ILogger<GetSavingsCalculatorData>>();
        var functionInstance = new GetSavingsCalculatorData(mockLogger.Object);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await functionInstance.Run(request, -1);

        Assert.NotNull(result);
        if (result is StatusCodeResult statusCodeResult)
        {
            Assert.Equal(StatusCodes.Status500InternalServerError, statusCodeResult.StatusCode);
        }
        else if (result is NotFoundObjectResult notFoundResult)
        {
            Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
        }
        else
        {
            Assert.Fail($"Unexpected result type returned: {result.GetType().Name}");
        }
    }
}
