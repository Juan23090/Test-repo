using EMA.Api.Functions.Donors;
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

namespace EMA.Api.Tests.Donors;

public sealed class CreateDonorJourneyEntryFunctionTests
{
    public CreateDonorJourneyEntryFunctionTests()
    {
        Environment.SetEnvironmentVariable("SqlConnectionString", "Server=localhost;Database=EMA;");
    }

    // Verifies function name and route mapping for donor journey entry creation endpoint
    [Fact]
    public void CreateDonorJourneyEntry_HasCorrectFunctionRouteAttribute()
    {
        var method = typeof(CreateDonorJourneyEntry).GetMethod(nameof(CreateDonorJourneyEntry.Run));
        Assert.NotNull(method);

        var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
        Assert.NotNull(functionAttr);
        Assert.Equal("CreateDonorJourneyEntry", functionAttr.Name);

        var httpTriggerParam = method.GetParameters().FirstOrDefault(p => p.GetCustomAttribute<HttpTriggerAttribute>() != null);
        Assert.NotNull(httpTriggerParam);

        var httpTriggerAttr = httpTriggerParam.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(httpTriggerAttr);
        Assert.Equal("donors/journey-history/{orgID:int}/{donorID:int}", httpTriggerAttr.Route);
    }

    // Verifies constructor accepts logger dependency
    [Fact]
    public void CreateDonorJourneyEntry_ConstructorInstantiatesWithLogger()
    {
        var mockLogger = new Mock<ILogger<CreateDonorJourneyEntry>>();
        var functionInstance = new CreateDonorJourneyEntry(mockLogger.Object);

        Assert.NotNull(functionInstance);
    }

    // Checks that posting malformed JSON payload catches deserialization errors and returns 500 ObjectResult
    [Fact]
    public async Task Run_WithMalformedJsonPayload_ReturnsErrorObjectResult()
    {
        var mockLogger = new Mock<ILogger<CreateDonorJourneyEntry>>();
        var functionInstance = new CreateDonorJourneyEntry(mockLogger.Object);

        var httpContext = new DefaultHttpContext();
        var malformedPayload = "{ invalid_json: true ";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(malformedPayload));

        var result = await functionInstance.Run(httpContext.Request, orgID: 18, donorID: 1);

        Assert.NotNull(result);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }
}
