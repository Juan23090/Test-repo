using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services;

public sealed class GraphEmailMessageNotFoundException : Exception
{
    public GraphEmailMessageNotFoundException(
        string operation)
        : base(
            $"The Graph email message was not found while {operation}.")
    {
    }
}

public sealed class GraphEmailService : IGraphEmailService
{
    private const string GraphBaseUrl =
        "https://graph.microsoft.com/v1.0";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphEmailService> _logger;

    private readonly string _tenantID;
    private readonly string _clientID;
    private readonly string _clientSecret;
    private readonly string _mailboxAddress;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public GraphEmailService(
        HttpClient httpClient,
        ILogger<GraphEmailService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _tenantID =
            RequiredEnvironmentVariable(
                "GraphTenantId");

        _clientID =
            RequiredEnvironmentVariable(
                "GraphClientId");

        _clientSecret =
            RequiredEnvironmentVariable(
                "GraphClientSecret");

        _mailboxAddress =
            RequiredEnvironmentVariable(
                "EtransferMailboxAddress");
    }

    public async Task<IReadOnlyList<GraphEmailMessage>>
        GetCandidateMessagesAsync(
            CancellationToken cancellationToken = default)
    {
        var mailbox =
            Uri.EscapeDataString(
                _mailboxAddress);

        var endpoint =
            $"{GraphBaseUrl}/users/{mailbox}/mailFolders/inbox/messages" +
            "?$top=25" +
            "&$orderby=receivedDateTime desc" +
            "&$select=id,subject,internetMessageId,receivedDateTime,from,toRecipients,replyTo";

        using var request =
            await CreateRequestAsync(
                HttpMethod.Get,
                endpoint,
                cancellationToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "listing inbox messages",
            cancellationToken);

        var json =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        var result =
            JsonSerializer.Deserialize<
                GraphMessageCollectionResponse>(
                    json,
                    JsonOptions);

        var messages =
            result?.Value
            ?? new List<GraphMessageDto>();

        return messages
            .Select(ToGraphEmailMessage)
            .Where(message =>
                !string.IsNullOrWhiteSpace(
                    message.Id))
            .ToList();
    }

    public async Task<byte[]> GetRawMimeAsync(
        string messageID,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageID))
        {
            throw new ArgumentException(
                "Message ID is required.",
                nameof(messageID));
        }

        var mailbox =
            Uri.EscapeDataString(
                _mailboxAddress);

        var escapedMessageID =
            Uri.EscapeDataString(
                messageID);

        var endpoint =
            $"{GraphBaseUrl}/users/{mailbox}/messages/{escapedMessageID}/$value";

        using var request =
            await CreateRequestAsync(
                HttpMethod.Get,
                endpoint,
                cancellationToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "downloading raw MIME email",
            cancellationToken);

        return await response.Content.ReadAsByteArrayAsync(
            cancellationToken);
    }

    public async Task MoveMessageAsync(
        string messageID,
        string destinationFolderName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageID))
        {
            throw new ArgumentException(
                "Message ID is required.",
                nameof(messageID));
        }

        if (string.IsNullOrWhiteSpace(
                destinationFolderName))
        {
            throw new ArgumentException(
                "Destination folder name is required.",
                nameof(destinationFolderName));
        }

        var folderID =
            await GetOrCreateMailFolderIDAsync(
                destinationFolderName,
                cancellationToken);

        var mailbox =
            Uri.EscapeDataString(
                _mailboxAddress);

        var escapedMessageID =
            Uri.EscapeDataString(
                messageID);

        var endpoint =
            $"{GraphBaseUrl}/users/{mailbox}/messages/{escapedMessageID}/move";

        var payload =
            JsonSerializer.Serialize(
                new
                {
                    destinationId = folderID
                });

        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                endpoint,
                cancellationToken);

        request.Content =
            new StringContent(
                payload,
                Encoding.UTF8,
                "application/json");

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            $"moving message to '{destinationFolderName}'",
            cancellationToken);
    }

    private async Task<string>
        GetOrCreateMailFolderIDAsync(
            string displayName,
            CancellationToken cancellationToken)
    {
        var mailbox =
            Uri.EscapeDataString(
                _mailboxAddress);

        var listEndpoint =
            $"{GraphBaseUrl}/users/{mailbox}/mailFolders" +
            "?$top=100" +
            "&$select=id,displayName";

        using var listRequest =
            await CreateRequestAsync(
                HttpMethod.Get,
                listEndpoint,
                cancellationToken);

        using var listResponse =
            await _httpClient.SendAsync(
                listRequest,
                cancellationToken);

        await EnsureSuccessAsync(
            listResponse,
            "listing mail folders",
            cancellationToken);

        var listJson =
            await listResponse.Content.ReadAsStringAsync(
                cancellationToken);

        var folders =
            JsonSerializer.Deserialize<
                GraphMailFolderCollectionResponse>(
                    listJson,
                    JsonOptions);

        var existing =
            folders?.Value.FirstOrDefault(
                folder =>
                    string.Equals(
                        folder.DisplayName,
                        displayName,
                        StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            return existing.Id;
        }

        var createEndpoint =
            $"{GraphBaseUrl}/users/{mailbox}/mailFolders";

        var payload =
            JsonSerializer.Serialize(
                new
                {
                    displayName
                });

        using var createRequest =
            await CreateRequestAsync(
                HttpMethod.Post,
                createEndpoint,
                cancellationToken);

        createRequest.Content =
            new StringContent(
                payload,
                Encoding.UTF8,
                "application/json");

        using var createResponse =
            await _httpClient.SendAsync(
                createRequest,
                cancellationToken);

        await EnsureSuccessAsync(
            createResponse,
            $"creating mail folder '{displayName}'",
            cancellationToken);

        var createJson =
            await createResponse.Content.ReadAsStringAsync(
                cancellationToken);

        var created =
            JsonSerializer.Deserialize<
                GraphMailFolderDto>(
                    createJson,
                    JsonOptions);

        if (string.IsNullOrWhiteSpace(created?.Id))
        {
            throw new InvalidOperationException(
                $"Graph did not return a folder ID for '{displayName}'.");
        }

        return created.Id;
    }

    private async Task<HttpRequestMessage>
        CreateRequestAsync(
            HttpMethod method,
            string endpoint,
            CancellationToken cancellationToken)
    {
        var token =
            await GetAccessTokenAsync(
                cancellationToken);

        var request =
            new HttpRequestMessage(
                method,
                endpoint);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "IdType=\"ImmutableId\"");

        return request;
    }

    private async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken)
            && _accessTokenExpiresAtUtc
                > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _accessToken;
        }

        var tokenEndpoint =
            $"https://login.microsoftonline.com/" +
            $"{Uri.EscapeDataString(_tenantID)}" +
            "/oauth2/v2.0/token";

        var form =
            new Dictionary<string, string>
            {
                ["client_id"] = _clientID,
                ["scope"] =
                    "https://graph.microsoft.com/.default",
                ["client_secret"] = _clientSecret,
                ["grant_type"] = "client_credentials"
            };

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                tokenEndpoint)
            {
                Content =
                    new FormUrlEncodedContent(
                        form)
            };

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Failed to get Graph access token. Status: {StatusCode}. Body: {Body}",
                (int)response.StatusCode,
                body);

            throw new InvalidOperationException(
                $"Failed to get Graph access token. Status: {(int)response.StatusCode}.");
        }

        var tokenResponse =
            JsonSerializer.Deserialize<
                GraphTokenResponse>(
                    body,
                    JsonOptions);

        if (string.IsNullOrWhiteSpace(
                tokenResponse?.AccessToken))
        {
            throw new InvalidOperationException(
                "Token response did not contain an access token.");
        }

        _accessToken =
            tokenResponse.AccessToken;

        var expiresInSeconds =
            tokenResponse.ExpiresIn > 0
                ? tokenResponse.ExpiresIn
                : 3600;

        _accessTokenExpiresAtUtc =
            DateTimeOffset.UtcNow.AddSeconds(
                Math.Max(
                    expiresInSeconds - 60,
                    60));

        return _accessToken;
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (response.StatusCode
                == HttpStatusCode.NotFound
            && IsGraphItemNotFound(body))
        {
            _logger.LogWarning(
                "Graph email message was not found while {Operation}. It may have already been moved or deleted by another ingestion instance.",
                operation);

            throw new GraphEmailMessageNotFoundException(
                operation);
        }

        _logger.LogError(
            "Graph error while {Operation}. Status: {StatusCode}. Body: {Body}",
            operation,
            (int)response.StatusCode,
            body);

        throw new InvalidOperationException(
            $"Graph error while {operation}. Status: {(int)response.StatusCode}.");
    }

    private static bool IsGraphItemNotFound(
        string responseBody)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    responseBody);

            return document.RootElement.TryGetProperty(
                    "error",
                    out var error)
                && error.TryGetProperty(
                    "code",
                    out var code)
                && string.Equals(
                    code.GetString(),
                    "ErrorItemNotFound",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeInteracDeposit(
        GraphMessageDto message)
    {
        var from =
            message.From
                ?.EmailAddress
                ?.Address;

        var subject =
            message.Subject
            ?? string.Empty;

        return string.Equals(
                from,
                "notify@payments.interac.ca",
                StringComparison.OrdinalIgnoreCase)
            && subject.Contains(
                "Interac e-Transfer",
                StringComparison.OrdinalIgnoreCase)
            && subject.Contains(
                "automatically deposited",
                StringComparison.OrdinalIgnoreCase);
    }

    private static GraphEmailMessage ToGraphEmailMessage(
        GraphMessageDto message)
    {
        return new GraphEmailMessage
        {
            Id =
                message.Id
                ?? string.Empty,

            Subject =
                message.Subject,

            InternetMessageId =
                message.InternetMessageId,

            ReceivedDateTime =
                message.ReceivedDateTime,

            FromAddress =
                message.From
                    ?.EmailAddress
                    ?.Address,

            FromName =
                message.From
                    ?.EmailAddress
                    ?.Name,

            RecipientEmailAddress =
                message.ToRecipients
                    ?.FirstOrDefault()
                    ?.EmailAddress
                    ?.Address,

            ReplyToAddress =
                message.ReplyTo
                    ?.FirstOrDefault()
                    ?.EmailAddress
                    ?.Address
        };
    }

    private static string RequiredEnvironmentVariable(
        string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException(
                $"Missing '{name}' environment variable.");
    }

    private sealed class GraphTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class GraphMessageCollectionResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMessageDto> Value { get; set; } =
            new();
    }

    private sealed class GraphMessageDto
    {
        public string? Id { get; set; }

        public string? Subject { get; set; }

        public string? InternetMessageId { get; set; }

        public DateTimeOffset? ReceivedDateTime
        {
            get;
            set;
        }

        public GraphEmailAddressContainerDto? From
        {
            get;
            set;
        }

        public List<GraphRecipientDto>? ToRecipients
        {
            get;
            set;
        }

        public List<GraphRecipientDto>? ReplyTo
        {
            get;
            set;
        }
    }

    private sealed class GraphRecipientDto
    {
        public GraphEmailAddressDto? EmailAddress
        {
            get;
            set;
        }
    }

    private sealed class GraphEmailAddressContainerDto
    {
        public GraphEmailAddressDto? EmailAddress
        {
            get;
            set;
        }
    }

    private sealed class GraphEmailAddressDto
    {
        public string? Name { get; set; }

        public string? Address { get; set; }
    }

    private sealed class GraphMailFolderCollectionResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMailFolderDto> Value { get; set; } =
            new();
    }

    private sealed class GraphMailFolderDto
    {
        public string? Id { get; set; }

        public string? DisplayName { get; set; }
    }
}