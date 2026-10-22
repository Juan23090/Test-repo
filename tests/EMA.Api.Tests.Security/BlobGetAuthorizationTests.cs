using EMA.Api.Services;
using EMA.Api.Services.Security;
using EMA_Api.Functions.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMA.Api.Tests.Security;

public sealed class BlobGetAuthorizationTests
{
    [Fact]
    public async Task DonorRequestingArbitraryOrganizationBlobIsDenied()
    {
        var identity = CreateDonorIdentity(organizationID: 15);
        var authorization = TenantBlobReadAccessPolicy.Evaluate(
            identity.ActorType,
            TenantBlobOrganizationReferenceKind.SensitiveOrganizationData,
            canAccessMenuItemMedia: false);

        var storage = new FakeBlobStorageService();
        var function = CreateFunction(authorization, storage);

        var result = await function.Run(
            CreateRequest(identity),
            "etl-blob-id");

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(storage.RequestedBlobIDs);
    }

    [Fact]
    public async Task DonorRequestingMediaForInaccessibleMenuItemIsDenied()
    {
        var identity = CreateDonorIdentity(organizationID: 15);
        var authorization = TenantBlobReadAccessPolicy.Evaluate(
            identity.ActorType,
            TenantBlobOrganizationReferenceKind.None,
            canAccessMenuItemMedia: false);

        var storage = new FakeBlobStorageService();
        var function = CreateFunction(authorization, storage);

        var result = await function.Run(
            CreateRequest(identity),
            "media-blob-id");

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(storage.RequestedBlobIDs);
    }

    [Fact]
    public async Task CrossOrganizationBlobRequestIsDeniedBeforeStorageRead()
    {
        var identity = CreateDonorIdentity(organizationID: 16);
        var storage = new FakeBlobStorageService();

        // The authorization service is responsible for establishing that the
        // requested resource is available to the current organization. BlobGet
        // must not probe any tenant namespace when that check fails.
        var function = CreateFunction(
            TenantBlobReadAuthorization.Denied,
            storage);

        var result = await function.Run(
            CreateRequest(identity),
            "org-15-media-key");

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(storage.RequestedBlobIDs);
    }

    [Fact]
    public async Task DonorRequestingAccessibleMenuItemMediaIsAllowed()
    {
        var identity = CreateDonorIdentity(organizationID: 15);
        var authorization = TenantBlobReadAccessPolicy.Evaluate(
            identity.ActorType,
            TenantBlobOrganizationReferenceKind.None,
            canAccessMenuItemMedia: true);

        var storage = new FakeBlobStorageService();
        storage.Add(
            TenantBlobName.ForOrganization(15, "media-key"),
            "audio/mp4");

        var function = CreateFunction(authorization, storage);

        var result = await function.Run(
            CreateRequest(identity),
            "media-key");

        Assert.IsType<FileStreamResult>(result);
        Assert.Equal(
            new[] { "org-15-media-key" },
            storage.RequestedBlobIDs);
        Assert.False(authorization.AllowLegacyFallback);
        Assert.True(authorization.AllowSharedMenuItemMedia);
    }

    [Fact]
    public async Task OrganizationAdministratorRequestingAccessibleMediaIsAllowed()
    {
        var identity = CreateOrganizationUserIdentity(
            organizationID: 15,
            roleIDs: new[] { 10 });

        var authorization = TenantBlobReadAccessPolicy.Evaluate(
            identity.ActorType,
            TenantBlobOrganizationReferenceKind.None,
            canAccessMenuItemMedia: true);

        var storage = new FakeBlobStorageService();
        storage.Add(
            TenantBlobName.ForOrganization(15, "media-key"),
            "audio/mp4");

        var function = CreateFunction(authorization, storage);

        var result = await function.Run(
            CreateRequest(identity),
            "media-key");

        Assert.IsType<FileStreamResult>(result);
        Assert.Equal(
            new[] { "org-15-media-key" },
            storage.RequestedBlobIDs);
    }

    [Fact]
    public async Task AccessibleMenuItemMediaFallsBackOnlyToDedicatedSharedNamespace()
    {
        var identity = CreateDonorIdentity(organizationID: 15);
        var authorization = TenantBlobReadAccessPolicy.Evaluate(
            identity.ActorType,
            TenantBlobOrganizationReferenceKind.None,
            canAccessMenuItemMedia: true);

        var storage = new FakeBlobStorageService();
        storage.Add(
            SharedMenuItemMediaBlobName.ForBlobID("media-key"),
            "audio/mpeg");

        var function = CreateFunction(authorization, storage);

        var result = await function.Run(
            CreateRequest(identity),
            "media-key");

        Assert.IsType<FileStreamResult>(result);
        Assert.Equal(
            new[]
            {
                TenantBlobName.ForOrganization(15, "media-key"),
                SharedMenuItemMediaBlobName.ForBlobID("media-key")
            },
            storage.RequestedBlobIDs);
        Assert.False(authorization.AllowLegacyFallback);
        Assert.True(authorization.AllowSharedMenuItemMedia);
    }

    [Fact]
    public async Task MenuItemMediaNeverFallsBackToGlobalLegacyBlob()
    {
        var identity = CreateDonorIdentity(organizationID: 15);
        var authorization = TenantBlobReadAccessPolicy.Evaluate(
            identity.ActorType,
            TenantBlobOrganizationReferenceKind.None,
            canAccessMenuItemMedia: true);

        var storage = new FakeBlobStorageService();
        storage.Add("media-key", "audio/mp4");

        var function = CreateFunction(authorization, storage);

        var result = await function.Run(
            CreateRequest(identity),
            "media-key");

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(
            new[]
            {
                "org-15-media-key",
                SharedMenuItemMediaBlobName.ForBlobID("media-key")
            },
            storage.RequestedBlobIDs);
        Assert.False(authorization.AllowLegacyFallback);
        Assert.True(authorization.AllowSharedMenuItemMedia);
    }

    private static BlobGet CreateFunction(
        TenantBlobReadAuthorization authorization,
        FakeBlobStorageService storage)
    {
        return new BlobGet(
            NullLogger<BlobGet>.Instance,
            storage,
            new FakeBlobAuthorizationService(
                authorization));
    }

    private static HttpRequest CreateRequest(
        ApiRequestIdentity identity)
    {
        var context = new DefaultHttpContext
        {
            User = identity.ToClaimsPrincipal()
        };

        return context.Request;
    }

    private static ApiRequestIdentity CreateDonorIdentity(
        int organizationID)
    {
        var issued = DateTimeOffset.UtcNow;

        return new ApiRequestIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.Donor,
            organizationID,
            UserID: 9001,
            DonorID: 501,
            RoleIDs: Array.Empty<int>(),
            AllowedCategoryIDs: new[] { 10, 30 },
            IssuedAtUtc: issued,
            ExpiresAtUtc: issued.AddHours(1));
    }

    private static ApiRequestIdentity CreateOrganizationUserIdentity(
        int organizationID,
        IReadOnlyList<int> roleIDs)
    {
        var issued = DateTimeOffset.UtcNow;

        return new ApiRequestIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.OrganizationUser,
            organizationID,
            UserID: 10,
            DonorID: null,
            RoleIDs: roleIDs,
            AllowedCategoryIDs: new[] { 10, 20 },
            IssuedAtUtc: issued,
            ExpiresAtUtc: issued.AddHours(1));
    }

    private sealed class FakeBlobAuthorizationService :
        ITenantBlobLegacyAuthorizationService
    {
        private readonly TenantBlobReadAuthorization _authorization;

        public FakeBlobAuthorizationService(
            TenantBlobReadAuthorization authorization)
        {
            _authorization = authorization;
        }

        public Task<TenantBlobReadAuthorization> AuthorizeReadAsync(
            ApiRequestIdentity identity,
            string blobID,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_authorization);
        }

        public Task<bool> IsReferencedByOrganizationAsync(
            int EMAOrganizationID,
            string blobID,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _authorization.AllowLegacyFallback);
        }
    }

    private sealed class FakeBlobStorageService :
        IBlobStorageService
    {
        private readonly Dictionary<string, BlobGetResult> _blobs =
            new(StringComparer.Ordinal);

        public List<string> RequestedBlobIDs { get; } = new();

        public void Add(
            string blobID,
            string contentType)
        {
            _blobs[blobID] = new BlobGetResult
            {
                Content = new MemoryStream(new byte[] { 1, 2, 3 }),
                ContentType = contentType,
                ContentLength = 3
            };
        }

        public Task BlobPutAsync(
            string blobID,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<BlobGetResult> BlobGetAsync(
            string blobID,
            CancellationToken cancellationToken = default)
        {
            RequestedBlobIDs.Add(blobID);

            if (!_blobs.TryGetValue(blobID, out var blob))
            {
                throw new FileNotFoundException();
            }

            return Task.FromResult(blob);
        }

        public Task<bool> BlobDeleteAsync(
            string blobID,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
