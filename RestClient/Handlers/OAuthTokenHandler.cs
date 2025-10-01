using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Exceptions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NameProject.RestClient.Handlers;

public class OAuthTokenHandler : DelegatingHandler
{
    private sealed record TokenState(AuthenticationHeaderValue Header, DateTimeOffset ExpiresAt);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<string, TokenState> TokenCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _serviceName;
    private readonly IOptionsMonitor<RestClientOptions> _optionsMonitor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OAuthTokenHandler> _logger;

    public OAuthTokenHandler(
        string serviceName,
        IOptionsMonitor<RestClientOptions> optionsMonitor,
        IHttpClientFactory httpClientFactory,
        ILogger<OAuthTokenHandler> logger)
    {
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        var tokenState = GetValidToken(_serviceName);
        if (tokenState is null)
        {
            var refreshLock = Locks.GetOrAdd(_serviceName, _ => new SemaphoreSlim(1, 1));
            await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                tokenState = GetValidToken(_serviceName);
                if (tokenState is null)
                {
                    tokenState = await RequestTokenAsync(serviceSetting.TokenRequest, cancellationToken).ConfigureAwait(false);
                    TokenCache[_serviceName] = tokenState;
                }
            }
            finally
            {
                refreshLock.Release();
            }
        }

        request.Headers.Authorization = tokenState.Header;
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static TokenState? GetValidToken(string serviceName)
        => TokenCache.TryGetValue(serviceName, out var state) && DateTimeOffset.UtcNow < state.ExpiresAt
            ? state
            : null;

    private async Task<TokenState> RequestTokenAsync(TokenSetting tokenSetting, CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        ConfigureHeaders(httpClient, tokenSetting.DefaultRequestHeaders);

        var requestUri = new Uri(tokenSetting.BaseAddress, tokenSetting.Path);
        using var content = CreateTokenContent(tokenSetting);

        var response = await httpClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Error requesting token for service {ServiceName}: {Error}", _serviceName, errorContent);
            throw new ApiException("Error acquiring token");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var token = JsonSerializer.Deserialize<TokenResponseDto>(payload, JsonOptions)
                    ?? throw new ApiException("Unable to deserialize token response");

        return new TokenState(new AuthenticationHeaderValue(token.TokenType, token.AccessToken),
                              DateTimeOffset.UtcNow.AddSeconds(token.ExpireIn));
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
        if (!string.Equals(tokenSetting.ContentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
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
