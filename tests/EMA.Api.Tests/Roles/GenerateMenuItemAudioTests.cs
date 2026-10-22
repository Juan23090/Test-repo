using EMA.Api.Functions.Roles;
using EMA.Api.Services;
using EMA.Api.Services.Audio;
using EMA.Api.Services.Navigation;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMA.Api.Tests.Roles;

public sealed class GenerateMenuItemAudioTests
{
    [Fact]
    public async Task MissingResourceReturnsNotFoundWithoutCallingProvider()
    {
        var persistence = new FakePersistence();
        var speech = new FakeTextToSpeech();
        var storage = new FakeBlobStorage();
        var function = CreateFunction(persistence, speech, storage);

        var result = await function.Run(Request(), 42, 99);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(0, speech.CallCount);
        Assert.Empty(storage.PutBlobIDs);
    }

    [Fact]
    public async Task UnavailableResourceTableReturnsConflict()
    {
        var persistence = new FakePersistence
        {
            TableExists = false
        };
        var function = CreateFunction(
            persistence,
            new FakeTextToSpeech(),
            new FakeBlobStorage());

        var result = await function.Run(Request(), 42, 99);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task NonAudioResourceIsRejected()
    {
        var persistence = PersistenceWith(
            new MenuItemAudioResourceSnapshot(
                99,
                42,
                "V",
                "Script",
                null));
        var speech = new FakeTextToSpeech();
        var function = CreateFunction(
            persistence,
            speech,
            new FakeBlobStorage());

        var result = await function.Run(Request(), 42, 99);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, speech.CallCount);
    }

    [Fact]
    public async Task AudioWithoutScriptIsRejected()
    {
        var persistence = PersistenceWith(
            Audio(script: null));
        var speech = new FakeTextToSpeech();
        var function = CreateFunction(
            persistence,
            speech,
            new FakeBlobStorage());

        var result = await function.Run(Request(), 42, 99);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, speech.CallCount);
    }

    [Fact]
    public async Task ProviderFailureDoesNotChangeBlobOrDatabase()
    {
        var persistence = PersistenceWith(Audio());
        var speech = new FakeTextToSpeech
        {
            Exception = new InvalidOperationException("provider unavailable")
        };
        var storage = new FakeBlobStorage();
        var function = CreateFunction(persistence, speech, storage);

        var result = await function.Run(Request(), 42, 99);

        var failure = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, failure.StatusCode);
        Assert.Empty(storage.PutBlobIDs);
        Assert.False(persistence.UpdateCalled);
    }

    [Fact]
    public async Task ResourceChangedDuringSynthesisReturnsConflictBeforeBlobWrite()
    {
        var persistence = PersistenceWith(
            Audio(script: "Original script"),
            Audio(script: "Edited script"));
        var speech = new FakeTextToSpeech();
        var storage = new FakeBlobStorage();
        var function = CreateFunction(persistence, speech, storage);

        var result = await function.Run(Request(), 42, 99);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Empty(storage.PutBlobIDs);
        Assert.False(persistence.UpdateCalled);
    }

    [Fact]
    public async Task SuccessfulGenerationStoresSharedMp3AndUpdatesResource()
    {
        var persistence = PersistenceWith(
            Audio(blobID: "old-recording"));
        var speech = new FakeTextToSpeech
        {
            Audio = new GeneratedSpeechAudio(
                [1, 2, 3, 4],
                "audio/mpeg")
        };
        var storage = new FakeBlobStorage();
        var function = CreateFunction(persistence, speech, storage);

        var result = await function.Run(Request(), 42, 99);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        Assert.Single(storage.PutBlobIDs);
        Assert.StartsWith("menu-media-", storage.PutBlobIDs[0]);
        Assert.Equal("audio/mpeg", storage.LastContentType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, storage.LastContent);
        Assert.True(persistence.UpdateCalled);
        Assert.Equal("old-recording", persistence.ExpectedOldBlobID);
        Assert.NotNull(persistence.NewBlobID);
        Assert.Equal(
            SharedMenuItemMediaBlobName.ForBlobID("old-recording"),
            Assert.Single(storage.DeletedBlobIDs));
    }

    [Fact]
    public async Task FailedConditionalUpdateDeletesNewBlobAndPreservesOldReference()
    {
        var persistence = PersistenceWith(
            Audio(blobID: "old-recording"));
        persistence.UpdateResult = false;
        var storage = new FakeBlobStorage();
        var function = CreateFunction(
            persistence,
            new FakeTextToSpeech(),
            storage);

        var result = await function.Run(Request(), 42, 99);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Single(storage.PutBlobIDs);
        Assert.Single(storage.DeletedBlobIDs);
        Assert.Equal(storage.PutBlobIDs[0], storage.DeletedBlobIDs[0]);
        Assert.Equal("old-recording", persistence.ExpectedOldBlobID);
    }

    [Fact]
    public async Task BlobWriteFailureNeverUpdatesDatabase()
    {
        var persistence = PersistenceWith(Audio());
        var storage = new FakeBlobStorage
        {
            PutException = new InvalidOperationException("storage unavailable")
        };
        var function = CreateFunction(
            persistence,
            new FakeTextToSpeech(),
            storage);

        var result = await function.Run(Request(), 42, 99);

        var failure = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
        Assert.False(persistence.UpdateCalled);
    }

    private static GenerateMenuItemAudio CreateFunction(
        FakePersistence persistence,
        FakeTextToSpeech speech,
        FakeBlobStorage storage) =>
        new(
            NullLogger<GenerateMenuItemAudio>.Instance,
            persistence,
            speech,
            storage);

    private static HttpRequest Request()
    {
        var context = new DefaultHttpContext();
        return context.Request;
    }

    private static MenuItemAudioResourceSnapshot Audio(
        string? script = "Welcome to EMA.",
        string? blobID = null) =>
        new(
            99,
            42,
            "A",
            script,
            blobID);

    private static FakePersistence PersistenceWith(
        params MenuItemAudioResourceSnapshot[] snapshots) =>
        new()
        {
            Snapshots = new Queue<MenuItemAudioResourceSnapshot?>(
                snapshots.Length == 1
                    ? new MenuItemAudioResourceSnapshot?[]
                    {
                        snapshots[0],
                        snapshots[0]
                    }
                    : snapshots.Cast<MenuItemAudioResourceSnapshot?>())
        };

    private sealed class FakePersistence :
        IMenuItemAudioGenerationPersistence
    {
        public bool TableExists { get; init; } = true;
        public Queue<MenuItemAudioResourceSnapshot?> Snapshots { get; init; } = new();
        public bool UpdateResult { get; set; } = true;
        public bool UpdateCalled { get; private set; }
        public string? ExpectedOldBlobID { get; private set; }
        public string? NewBlobID { get; private set; }

        public Task<bool> TableExistsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(TableExists);

        public Task<MenuItemAudioResourceSnapshot?> GetAudioResourceAsync(
            int menuItemID,
            int menuItemRowID,
            CancellationToken cancellationToken)
        {
            if (Snapshots.Count == 0)
            {
                return Task.FromResult<MenuItemAudioResourceSnapshot?>(null);
            }

            var next = Snapshots.Dequeue();

            if (Snapshots.Count == 0 && next is not null)
            {
                Snapshots.Enqueue(next);
            }

            return Task.FromResult(next);
        }

        public Task<bool> TryUpdateGeneratedBlobAsync(
            int menuItemID,
            int menuItemRowID,
            string expectedAudioScript,
            string? expectedBlobID,
            string newBlobID,
            CancellationToken cancellationToken)
        {
            UpdateCalled = true;
            ExpectedOldBlobID = expectedBlobID;
            NewBlobID = newBlobID;
            return Task.FromResult(UpdateResult);
        }
    }

    private sealed class FakeTextToSpeech : ITextToSpeechService
    {
        public int CallCount { get; private set; }
        public Exception? Exception { get; init; }
        public GeneratedSpeechAudio Audio { get; init; } =
            new([7, 8, 9], "audio/mpeg");

        public Task<GeneratedSpeechAudio> GenerateMp3Async(
            string text,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Audio);
        }
    }

    private sealed class FakeBlobStorage : IBlobStorageService
    {
        public List<string> PutBlobIDs { get; } = new();
        public List<string> DeletedBlobIDs { get; } = new();
        public byte[]? LastContent { get; private set; }
        public string? LastContentType { get; private set; }
        public Exception? PutException { get; init; }

        public async Task BlobPutAsync(
            string blobID,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (PutException is not null)
            {
                throw PutException;
            }

            PutBlobIDs.Add(blobID);
            LastContentType = contentType;
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            LastContent = memory.ToArray();
        }

        public Task<BlobGetResult> BlobGetAsync(
            string blobID,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> BlobDeleteAsync(
            string blobID,
            CancellationToken cancellationToken = default)
        {
            DeletedBlobIDs.Add(blobID);
            return Task.FromResult(true);
        }
    }
}
