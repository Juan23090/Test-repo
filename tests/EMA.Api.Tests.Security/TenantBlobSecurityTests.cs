using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;

namespace EMA.Api.Tests.Security;

public sealed class TenantBlobSecurityTests
{
    [Fact]
    public void BlobNameIsBoundToOrganization()
    {
        Assert.Equal(
            "org-17-abc123",
            TenantBlobName.ForOrganization(
                17,
                "abc123"));

        Assert.NotEqual(
            TenantBlobName.ForOrganization(
                17,
                "abc123"),
            TenantBlobName.ForOrganization(
                18,
                "abc123"));
    }

    [Fact]
    public async Task SpoofedImageContentIsRejected()
    {
        var context =
            new DefaultHttpContext();

        var content =
            "not an image"u8.ToArray();

        context.Request.ContentType =
            "image/png";

        context.Request.ContentLength =
            content.Length;

        context.Request.Body =
            new MemoryStream(
                content);

        await Assert.ThrowsAsync<
            ArgumentException>(
            () =>
                TenantBlobUploadValidator
                    .ValidateAsync(
                        context.Request));
    }

    [Fact]
    public async Task ValidPngPrefixIsPreserved()
    {
        byte[] content =
        [
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A,
            0x01,
            0x02
        ];

        var context =
            new DefaultHttpContext();

        context.Request.ContentType =
            "image/png";

        context.Request.ContentLength =
            content.Length;

        context.Request.Body =
            new MemoryStream(
                content);

        var validated =
            await TenantBlobUploadValidator
                .ValidateAsync(
                    context.Request);

        using var copy =
            new MemoryStream();

        await validated.Content.CopyToAsync(
            copy);

        Assert.Equal(
            content,
            copy.ToArray());
    }
}

