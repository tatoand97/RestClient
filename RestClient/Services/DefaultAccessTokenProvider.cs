using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Exceptions;
using NameProject.RestClient.Interfaces;
using NameProject.RestClient.Models;

namespace NameProject.RestClient.Services;

public sealed class DefaultAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    IRestClientSerializer serializer,
    ILogger<DefaultAccessTokenProvider> logger) : IAccessTokenProvider
{
    private const string DefaultContentType = "application/x-www-form-urlencoded";

    private readonly ConcurrentDictionary<string, AccessToken> _tokenCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<AccessToken> GetTokenAsync(string clientName, AuthOptions options, CancellationToken cancellationToken)
    {
        ValidateAuthOptions(clientName, options);

        var cacheKey = CreateCacheKey(clientName, options);
        if (TryGetValidToken(cacheKey, out var cachedToken))
        {
            return cachedToken;
        }

        var tokenLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetValidToken(cacheKey, out cachedToken))
            {
                return cachedToken;
            }

            var token = await RequestTokenAsync(clientName, options, cancellationToken).ConfigureAwait(false);
            _tokenCache[cacheKey] = token;
            return token;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    public Task InvalidateAsync(string clientName, CancellationToken cancellationToken)
    {
        var prefix = string.Concat(clientName, "|");
        foreach (var key in _tokenCache.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            _tokenCache.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    private bool TryGetValidToken(string cacheKey, out AccessToken token)
    {
        if (_tokenCache.TryGetValue(cacheKey, out var cachedToken) && DateTimeOffset.UtcNow < cachedToken.ExpiresAt)
        {
            token = cachedToken;
            return true;
        }

        token = default!;
        return false;
    }

    private async Task<AccessToken> RequestTokenAsync(string clientName, AuthOptions options, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenUrl);
        ConfigureHeaders(request, options);

        var content = CreateTokenContent(options);
        if (content is not null)
        {
            request.Content = content;
        }

        using var httpClient = httpClientFactory.CreateClient();
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Token request for client {ClientName} failed with status code {StatusCode}. Response body: {ResponseBody}",
                clientName,
                (int)response.StatusCode,
                DefaultHttpErrorHandler.Truncate(responseBody));

            throw new ExternalApiException(
                response.StatusCode,
                response.ReasonPhrase,
                request.RequestUri,
                request.Method,
                DefaultHttpErrorHandler.Truncate(responseBody),
                $"Token request for client {clientName} failed with status code {(int)response.StatusCode}.");
        }

        TokenResponseDto tokenResponse;
        try
        {
            tokenResponse = serializer.Deserialize<TokenResponseDto>(responseBody);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Token response for client {clientName} could not be deserialized.", ex);
        }

        if (string.IsNullOrWhiteSpace(tokenResponse.TokenType) || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException($"Token response for client {clientName} is missing token_type or access_token.");
        }

        return new AccessToken(
            tokenResponse.TokenType,
            tokenResponse.AccessToken,
            CalculateExpiresAt(tokenResponse.ExpiresIn));
    }

    private static void ValidateAuthOptions(string clientName, AuthOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Type == AuthenticationType.None)
        {
            throw new InvalidOperationException($"Client {clientName} does not require OAuth tokens.");
        }

        if (options.TokenUrl is null || !options.TokenUrl.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Client {clientName} must define an absolute Auth:TokenUrl.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException($"Client {clientName} must define Auth:ClientId.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException($"Client {clientName} must define Auth:ClientSecret.");
        }
    }

    private static string CreateCacheKey(string clientName, AuthOptions options)
        => string.Join(
            "|",
            clientName,
            options.TokenUrl?.AbsoluteUri ?? string.Empty,
            options.ClientId ?? string.Empty,
            options.Scope,
            options.Audience);

    private static DateTimeOffset CalculateExpiresAt(int expiresIn)
    {
        var safeExpiresIn = expiresIn <= 0 ? 60 : expiresIn;
        var buffer = Math.Clamp(safeExpiresIn / 10, 5, 60);
        return DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, safeExpiresIn - buffer));
    }

    private static HttpContent? CreateTokenContent(AuthOptions options)
    {
        if (!options.SendRequestBody)
        {
            return null;
        }

        var body = CreateTokenRequestBody(options);
        if (string.Equals(options.ContentType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, options.ContentType);
        }

        var content = new FormUrlEncodedContent(body);
        if (!string.Equals(options.ContentType, DefaultContentType, StringComparison.OrdinalIgnoreCase))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(options.ContentType);
        }

        return content;
    }

    private static Dictionary<string, string> CreateTokenRequestBody(AuthOptions options)
    {
        var body = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["grant_type"] = options.GrantType
        };

        if (!string.IsNullOrWhiteSpace(options.Scope))
        {
            body["scope"] = options.Scope;
        }

        if (!string.IsNullOrWhiteSpace(options.Audience))
        {
            body["audience"] = options.Audience;
        }

        if (options.Type == AuthenticationType.OAuth2Body)
        {
            body["client_id"] = options.ClientId!;
            body["client_secret"] = options.ClientSecret!;
        }

        return body;
    }

    private static void ConfigureHeaders(HttpRequestMessage request, AuthOptions options)
    {
        foreach (var header in options.DefaultRequestHeaders)
        {
            request.Headers.Remove(header.Key);
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (options.Type == AuthenticationType.OAuth2Header)
        {
            TryAddHeader(request, "ClientId", options.ClientId);
            TryAddHeader(request, "ClientSecret", options.ClientSecret);
            TryAddHeader(request, "GrantType", options.GrantType);
        }

        static void TryAddHeader(HttpRequestMessage request, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !request.Headers.Contains(key))
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }
    }
}
