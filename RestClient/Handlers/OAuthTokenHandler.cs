using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Exceptions;

namespace NameProject.RestClient.Handlers;

public class OAuthTokenHandler(
    string serviceName,
    IOptionsMonitor<RestClientOptions> optionsMonitor,
    IHttpClientFactory httpClientFactory,
    ILogger<OAuthTokenHandler> logger) : DelegatingHandler
{
    private sealed record TokenState(AuthenticationHeaderValue Header, DateTimeOffset ExpiresAt);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<string, TokenState> TokenCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
    private readonly IOptionsMonitor<RestClientOptions> _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<OAuthTokenHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    internal static void InvalidateToken(string serviceName)
        => TokenCache.TryRemove(serviceName, out _);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue ?? throw new InvalidOperationException("RestClient options not available.");
        if (!options.Services.TryGetValue(_serviceName, out var serviceSetting))
        {
            throw new InvalidOperationException($"Service {_serviceName} is not configured.");
        }

        if (serviceSetting.TokenRequest is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var tokenState = await EnsureTokenAsync(serviceSetting.TokenRequest, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = tokenState.Header;

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TokenState> EnsureTokenAsync(TokenSetting tokenSetting, CancellationToken cancellationToken)
    {
        var cachedToken = GetValidToken(_serviceName);
        if (cachedToken is not null)
        {
            return cachedToken;
        }

        var refreshLock = Locks.GetOrAdd(_serviceName, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedToken = GetValidToken(_serviceName);
            if (cachedToken is not null)
            {
                return cachedToken;
            }

            var tokenState = await RequestTokenAsync(tokenSetting, cancellationToken).ConfigureAwait(false);
            TokenCache[_serviceName] = tokenState;
            return tokenState;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static TokenState? GetValidToken(string serviceName)
        => TokenCache.TryGetValue(serviceName, out var state) && DateTimeOffset.UtcNow < state.ExpiresAt
            ? state
            : null;

    private async Task<TokenState> RequestTokenAsync(TokenSetting tokenSetting, CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        ConfigureHeaders(httpClient, tokenSetting.DefaultRequestHeaders);

        var authenticationHeader = tokenSetting.GetClientAuthenticationHeader();
        if (authenticationHeader is not null)
        {
            httpClient.DefaultRequestHeaders.Authorization = authenticationHeader;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenSetting.TokenUrl)
        {
            Content = CreateTokenContent(tokenSetting)
        };

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Error requesting token for service {ServiceName}: {StatusCode} - {Error}", _serviceName, response.StatusCode, errorContent);
            throw new ApiException("Error acquiring token");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var token = JsonSerializer.Deserialize<TokenResponseDto>(payload, JsonOptions)
                    ?? throw new ApiException("Unable to deserialize token response");

        if (string.IsNullOrWhiteSpace(token.TokenType) || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new ApiException("Token response is missing required fields.");
        }

        return new TokenState(new AuthenticationHeaderValue(token.TokenType, token.AccessToken), CalculateExpiration(token));
    }

    private static DateTimeOffset CalculateExpiration(TokenResponseDto token)
    {
        var expiresIn = token.ExpireIn <= 0 ? 60 : token.ExpireIn;
        var buffer = Math.Clamp(expiresIn / 10, 5, 60);
        var effectiveSeconds = Math.Max(1, expiresIn - buffer);
        return DateTimeOffset.UtcNow.AddSeconds(effectiveSeconds);
    }

    private static HttpContent CreateTokenContent(TokenSetting tokenSetting)
    {
        var body = tokenSetting.GetTokenRequestBody();
        if (string.Equals(tokenSetting.ContentType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            return new StringContent(json, Encoding.UTF8, tokenSetting.ContentType);
        }

        var content = new FormUrlEncodedContent(body);
        if (!string.Equals(tokenSetting.ContentType, TokenSetting.DefaultContentType, StringComparison.OrdinalIgnoreCase))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(tokenSetting.ContentType);
        }

        return content;
    }

    private static void ConfigureHeaders(HttpClient client, Dictionary<string, string> headers)
    {
        client.DefaultRequestHeaders.Clear();
        foreach (var header in headers)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }
}
