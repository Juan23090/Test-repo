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

public sealed class GetOrganizationFollowUpQueueFunctionTests
{
    public GetOrganizationFollowUpQueueFunctionTests()
    {
        Environment.SetEnvironmentVariable("SqlConnectionString", "Server=localhost;Database=EMA;");
    }

    // Verifies function name and route mapping for organization follow-up queue endpoint
    [Fact]
    public void GetOrganizationFollowUpQueue_HasCorrectFunctionRouteAttribute()
    {
        var method = typeof(GetOrganizationFollowUpQueue).GetMethod(nameof(GetOrganizationFollowUpQueue.Run));
        Assert.NotNull(method);

        var functionAttr = method.GetCustomAttribute<FunctionAttribute>();
        Assert.NotNull(functionAttr);
        Assert.Equal("GetOrganizationFollowUpQueue", functionAttr.Name);

        var httpTriggerParam = method.GetParameters().FirstOrDefault(p => p.GetCustomAttribute<HttpTriggerAttribute>() != null);
        Assert.NotNull(httpTriggerParam);

        var httpTriggerAttr = httpTriggerParam.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(httpTriggerAttr);
        Assert.Equal("organization/followups", httpTriggerAttr.Route);
    }

    // Verifies constructor accepts logger dependency
    [Fact]
    public void GetOrganizationFollowUpQueue_ConstructorInstantiatesWithLogger()
    {
        var mockLogger = new Mock<ILogger<GetOrganizationFollowUpQueue>>();
        var functionInstance = new GetOrganizationFollowUpQueue(mockLogger.Object);

        Assert.NotNull(functionInstance);
    }

    // Checks that invoking Run for follow-up queue handles database errors gracefully
    [Fact]
    public async Task Run_ForFollowUpQueue_HandlesDatabaseErrorsGracefully()
    {
        var mockLogger = new Mock<ILogger<GetOrganizationFollowUpQueue>>();
        var functionInstance = new GetOrganizationFollowUpQueue(mockLogger.Object);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await functionInstance.Run(request);

        Assert.NotNull(result);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }
}
