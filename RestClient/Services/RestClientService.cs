using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Options;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Exceptions;
using NameProject.RestClient.Handlers;
using NameProject.RestClient.Interfaces;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Polly.Retry;

namespace NameProject.RestClient.Services;

public class RestClientService(IHttpClientFactory clientFactory, IOptionsMonitor<RestClientOptions> optionsMonitor, ILogger<RestClientService> logger) : IRestClientService
{
    private const int MaxLoggedBodyLength = 2048;

    private readonly IHttpClientFactory _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    private readonly ILogger<RestClientService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IOptionsMonitor<RestClientOptions> _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy(RestClientOptions options)
    {
        if (options.HttpClientRetry <= 0)
        {
            throw new InvalidOperationException("Configuration value 'RestClient:HttpClientRetry' must be greater than zero.");
        }

        if (options.HttpClientDelay <= 0)
        {
            throw new InvalidOperationException("Configuration value 'RestClient:HttpClientDelay' must be greater than zero.");
        }

        var delay = TimeSpan.FromSeconds(options.HttpClientDelay);
        var sleepDurations = Backoff.DecorrelatedJitterBackoffV2(delay, options.HttpClientRetry);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                sleepDurations,
                (result, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {TimeSpan} due to {StatusCode}", retryCount, timeSpan, result.Result?.StatusCode);
                });
    }

    private async Task<T> ParseResponse<T>(string service, string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                   ?? throw new RestRequestFailedException($"Response body deserialized to null for service {service} and path {path}.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error deserializing response for service {Service}, path {Path} into {Type}. Payload: {Payload}",
                service,
                path,
                typeof(T).Name,
                TruncateForLog(content));
            throw new RestRequestFailedException("Error deserializing response body.", ex);
        }
    }

    private async Task<HttpResponseMessage> ExecuteWithHandling(
        string service,
        RestClientServiceSetting serviceSetting,
        string path,
        Func<HttpClient, string, CancellationToken, Task<HttpResponseMessage>> call,
        CancellationToken cancellationToken)
    {
        var response = await InvokeCallAsync(service, serviceSetting, path, call, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusAsync(service, path, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private async Task EnsureSuccessStatusAsync(string service, string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        LogErrorResponse(service, path, response, body);
        throw CreateRestRequestFailedException(response, body);
    }

    private void LogErrorResponse(string service, string path, HttpResponseMessage response, string body)
    {
        var request = response.RequestMessage;
        var method = request?.Method.Method ?? "UNKNOWN";
        var uri = request?.RequestUri?.ToString() ?? $"{service}:{path}";
        var reason = response.ReasonPhrase ?? "No reason phrase";

        _logger.LogError(
            "HTTP {Method} {Uri} for service {Service} failed with status code {StatusCode} ({Reason}). Response body: {Body}",
            method,
            uri,
            service,
            (int)response.StatusCode,
            reason,
            TruncateForLog(body));
    }

    private static RestRequestFailedException CreateRestRequestFailedException(HttpResponseMessage response, string body)
    {
        var request = response.RequestMessage;
        var method = request?.Method.Method ?? "UNKNOWN";
        var uri = request?.RequestUri?.ToString() ?? "unknown";
        var reason = response.ReasonPhrase ?? "No reason phrase";

        var message = new StringBuilder()
            .Append($"HTTP {method} {uri} failed with status code {(int)response.StatusCode} ({reason}).");

        if (!string.IsNullOrWhiteSpace(body))
        {
            message.Append(' ')
                   .Append("Response body: ")
                   .Append(body);
        }

        return new RestRequestFailedException(response.StatusCode, message.ToString());
    }

    private static string TruncateForLog(string value)
        => string.IsNullOrEmpty(value) || value.Length <= MaxLoggedBodyLength
            ? value
            : string.Concat(value.AsSpan(0, MaxLoggedBodyLength), "...(truncated)");

    private async Task<HttpResponseMessage> InvokeCallAsync(
        string service,
        RestClientServiceSetting serviceSetting,
        string path,
        Func<HttpClient, string, CancellationToken, Task<HttpResponseMessage>> call,
        CancellationToken cancellationToken)
    {
        if (serviceSetting.BaseAddress is null)
            throw new InvalidOperationException($"Service {service} does not have a base address configured.");

        try
        {
            var client = _clientFactory.CreateClient(service);
            var endpoint = new Uri(serviceSetting.BaseAddress, path).ToString();

            async Task<HttpResponseMessage> ExecuteAsync(CancellationToken ct)
                => await call(client, endpoint, ct).ConfigureAwait(false);

            var response = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && serviceSetting.TokenRequest is not null)
            {
                _logger.LogInformation("Received 401 for service {Service}. Refreshing token and retrying once.", service);
                response.Dispose();
                OAuthTokenHandler.InvalidateToken(service);
                response = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing http call for service {Service}, path {Path}", service, path);
            throw new RestRequestFailedException("Error executing HTTP call", ex);
        }
    }

    private static RestClientServiceSetting GetServiceSetting(string service, RestClientOptions options)
        => options.Services.TryGetValue(service, out var serviceSetting)
            ? serviceSetting
            : throw new InvalidOperationException($"Service {service} not found.");

    public Task<HttpResponseMessage> Delete(string service, string path)
        => Delete(service, path, CancellationToken.None);

    public Task<HttpResponseMessage> Delete(string service, string path, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        var policy = CreateRetryPolicy(options);

        return policy.ExecuteAsync(
            ct => ExecuteWithHandling(
                service,
                serviceSetting,
                path,
                static (client, endpoint, innerCt) => client.DeleteAsync(endpoint, innerCt),
                ct),
            cancellationToken);
    }

    public Task<T> Delete<T>(string service, string path)
        => Delete<T>(service, path, CancellationToken.None);

    public async Task<T> Delete<T>(string service, string path, CancellationToken cancellationToken)
    {
        using var response = await Delete(service, path, cancellationToken).ConfigureAwait(false);
        return await ParseResponse<T>(service, path, response, cancellationToken).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> Get(string service, string path)
        => Get(service, path, CancellationToken.None);

    public Task<HttpResponseMessage> Get(string service, string path, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        var policy = CreateRetryPolicy(options);

        return policy.ExecuteAsync(
            ct => ExecuteWithHandling(
                service,
                serviceSetting,
                path,
                static (client, endpoint, innerCt) => client.GetAsync(endpoint, innerCt),
                ct),
            cancellationToken);
    }

    public Task<T> Get<T>(string service, string path)
        => Get<T>(service, path, CancellationToken.None);

    public async Task<T> Get<T>(string service, string path, CancellationToken cancellationToken)
    {
        using var response = await Get(service, path, cancellationToken).ConfigureAwait(false);
        return await ParseResponse<T>(service, path, response, cancellationToken).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> Post(string service, string path, object payload)
        => Post(service, path, payload, CancellationToken.None);

    public Task<HttpResponseMessage> Post(string service, string path, object payload, CancellationToken cancellationToken)
    {
        var serializedPayload = JsonSerializer.Serialize(payload, JsonOptions);
        return ExecutePost(service, path, serializedPayload, cancellationToken);
    }

    public Task<HttpResponseMessage> Post(string service, string path, string payload)
        => Post(service, path, payload, CancellationToken.None);

    public Task<HttpResponseMessage> Post(string service, string path, string payload, CancellationToken cancellationToken)
        => ExecutePost(service, path, payload, cancellationToken);

    public Task<T> Post<T>(string service, string path, string payload)
        => Post<T>(service, path, payload, CancellationToken.None);

    public async Task<T> Post<T>(string service, string path, string payload, CancellationToken cancellationToken)
    {
        using var response = await Post(service, path, payload, cancellationToken).ConfigureAwait(false);
        return await ParseResponse<T>(service, path, response, cancellationToken).ConfigureAwait(false);
    }

    public Task<T> Post<T>(string service, string path, object payload)
        => Post<T>(service, path, payload, CancellationToken.None);

    public async Task<T> Post<T>(string service, string path, object payload, CancellationToken cancellationToken)
    {
        using var response = await Post(service, path, payload, cancellationToken).ConfigureAwait(false);
        return await ParseResponse<T>(service, path, response, cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> ExecutePost(string service, string path, string content, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        var policy = CreateRetryPolicy(options);

        return policy.ExecuteAsync(
            ct => ExecuteWithHandling(
                service,
                serviceSetting,
                path,
                (client, endpoint, innerCt) => client.PostAsync(endpoint, new StringContent(content, Encoding.UTF8, "application/json"), innerCt),
                ct),
            cancellationToken);
    }

    public Task<HttpResponseMessage> Put(string service, string path, object payload)
        => Put(service, path, payload, CancellationToken.None);

    public Task<HttpResponseMessage> Put(string service, string path, object payload, CancellationToken cancellationToken)
    {
        var serializedPayload = JsonSerializer.Serialize(payload, JsonOptions);
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        var policy = CreateRetryPolicy(options);

        return policy.ExecuteAsync(
            ct => ExecuteWithHandling(
                service,
                serviceSetting,
                path,
                (client, endpoint, innerCt) => client.PutAsync(endpoint, new StringContent(serializedPayload, Encoding.UTF8, "application/json"), innerCt),
                ct),
            cancellationToken);
    }

    public Task<T> Put<T>(string service, string path, object payload)
        => Put<T>(service, path, payload, CancellationToken.None);

    public async Task<T> Put<T>(string service, string path, object payload, CancellationToken cancellationToken)
    {
        using var response = await Put(service, path, payload, cancellationToken).ConfigureAwait(false);
        return await ParseResponse<T>(service, path, response, cancellationToken).ConfigureAwait(false);
    }
}
