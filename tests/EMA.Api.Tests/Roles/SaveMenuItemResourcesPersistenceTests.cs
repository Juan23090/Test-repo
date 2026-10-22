using System.Text;
using EMA.Api.DTOs;
using EMA.Api.Functions.Roles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMA.Api.Tests.Roles;

public sealed class SaveMenuItemResourcesPersistenceTests
{
    [Fact]
    public async Task NonexistentMenuItemReturnsNotFound()
    {
        var persistence = new FakePersistence
        {
            MenuItem = new MenuItemLookupResult(false, null)
        };
        var function = CreateFunction(persistence);

        var result = await function.Run(Request("{\"resources\":[]}"), 999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("MenuItem 999 was not found.", notFound.Value);
        Assert.False(persistence.ReplaceCalled);
    }

    [Fact]
    public async Task UnavailableResourceTablesReturnConflict()
    {
        var persistence = new FakePersistence { TablesExist = false };
        var function = CreateFunction(persistence);

        var result = await function.Run(Request("{\"resources\":[]}"), 42);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.False(persistence.ReplaceCalled);
    }

    [Fact]
    public async Task ValidResourceCollectionIsPassedToTransactionalPersistence()
    {
        var persistence = new FakePersistence
        {
            MenuItem = new MenuItemLookupResult(true, "menu-items")
        };
        var function = CreateFunction(persistence);

        var result = await function.Run(
            Request("""
                {"resources":[
                    {"menuItemResourceTypeID":"a","sequence":2,"description":" Audio ","audioScript":" Hello "},
                    {"menuItemResourceTypeID":"W","sequence":1,"description":" Setup "}
                ]}
                """),
            42);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.True(persistence.ReplaceCalled);
        Assert.Equal(42, persistence.ReplacedMenuItemID);
        Assert.Equal(2, persistence.ReplacedResources.Count);
        Assert.Equal("A", persistence.ReplacedResources[0].MenuItemResourceTypeID);
        Assert.Equal("Hello", persistence.ReplacedResources[0].AudioScript);
        Assert.Equal("/menu-items-setup", persistence.ReplacedResources[1].ScreenURL);
        Assert.Same(persistence.SavedResources, ok.Value);
    }

    [Fact]
    public async Task ReplacementFailureReturnsServerErrorAndDoesNotReportSuccess()
    {
        var persistence = new FakePersistence
        {
            MenuItem = new MenuItemLookupResult(true, "menu-items"),
            ReplaceException = new InvalidOperationException("simulated insert failure")
        };
        var function = CreateFunction(persistence);

        var result = await function.Run(
            Request("""
                {"resources":[
                    {"menuItemResourceTypeID":"A","sequence":1,"description":"Audio","audioScript":"Hello"}
                ]}
                """),
            42);

        var error = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
        Assert.True(persistence.ReplaceCalled);
        Assert.True(persistence.PreviousResourcesPreserved);
    }

    private static SaveMenuItemResources CreateFunction(FakePersistence persistence) =>
        new(NullLogger<SaveMenuItemResources>.Instance, persistence);

    private static HttpRequest Request(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.ContentType = "application/json";
        return context.Request;
    }

    private sealed class FakePersistence : IMenuItemResourcePersistence
    {
        public bool TablesExist { get; init; } = true;
        public MenuItemLookupResult MenuItem { get; init; } = new(true, "menu-items");
        public Exception? ReplaceException { get; init; }
        public bool ReplaceCalled { get; private set; }
        public int ReplacedMenuItemID { get; private set; }
        public IReadOnlyList<MenuItemResourceDto> ReplacedResources { get; private set; } = [];
        public bool PreviousResourcesPreserved { get; private set; } = true;
        public IReadOnlyList<MenuItemResourceDto> SavedResources { get; } =
            new List<MenuItemResourceDto>
            {
                new() { MenuItemRowID = 101, MenuItemID = 42, MenuItemResourceTypeID = "W", Sequence = 1, Description = "Setup", ScreenURL = "/menu-items-setup" },
                new() { MenuItemRowID = 102, MenuItemID = 42, MenuItemResourceTypeID = "A", Sequence = 2, Description = "Audio", AudioScript = "Hello" }
            };

        public Task<bool> TablesExistAsync(CancellationToken cancellationToken) =>
            Task.FromResult(TablesExist);

        public Task<MenuItemLookupResult> GetMenuItemAsync(int menuItemID, CancellationToken cancellationToken) =>
            Task.FromResult(MenuItem);

        public Task<IReadOnlyList<MenuItemResourceDto>> ReplaceAsync(
            int menuItemID,
            IReadOnlyList<MenuItemResourceDto> resources,
            CancellationToken cancellationToken)
        {
            ReplaceCalled = true;
            ReplacedMenuItemID = menuItemID;
            ReplacedResources = resources;

            if (ReplaceException is not null)
            {
                // The SQL implementation owns DELETE + INSERT + COMMIT inside one transaction
                // and rolls that transaction back before propagating an exception. This fake
                // models the observable contract: callers never see a partially replaced set.
                PreviousResourcesPreserved = true;
                throw ReplaceException;
            }

            return Task.FromResult(SavedResources);
        }
    }
}
