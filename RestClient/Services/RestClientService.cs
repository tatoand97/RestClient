using System.Net;
using System.Text;
using System.Text.Json;
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

public class RestClientService : IRestClientService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<RestClientService> _logger;
    private readonly IOptionsMonitor<RestClientOptions> _optionsMonitor;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RestClientService(IHttpClientFactory clientFactory, IOptionsMonitor<RestClientOptions> optionsMonitor, ILogger<RestClientService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy(RestClientOptions options)
        => HttpPolicyExtensions
               .HandleTransientHttpError()
               .WaitAndRetryAsync(
                   Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(options.HttpClientDelay), options.HttpClientRetry),
                   (result, timeSpan, retryCount, context) =>
                   {
                       _logger.LogWarning("Retry {RetryCount} after {TimeSpan} due to {StatusCode}", retryCount, timeSpan, result.Result?.StatusCode);
                   });

    private static async Task<T> ParseResponse<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new RestRequestFailedException(response.StatusCode, content);

        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    private async Task<HttpResponseMessage> InvokeCallAsync(string service, RestClientServiceSetting serviceSetting, string path, Func<HttpClient, string, Task<HttpResponseMessage>> call)
    {
        if (serviceSetting.BaseAddress is null)
            throw new InvalidOperationException($"Service {service} does not have a base address configured.");

        try
        {
            var client = _clientFactory.CreateClient(service);
            var endpoint = new Uri(serviceSetting.BaseAddress, path).ToString();

            async Task<HttpResponseMessage> ExecuteAsync()
                => await call(client, endpoint).ConfigureAwait(false);

            var response = await ExecuteAsync().ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && serviceSetting.TokenRequest is not null)
            {
                _logger.LogInformation("Received 401 for service {Service}. Refreshing token and retrying once.", service);
                response.Dispose();
                OAuthTokenHandler.InvalidateToken(service);
                response = await ExecuteAsync().ConfigureAwait(false);
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
    {
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        return CreateRetryPolicy(options).ExecuteAsync(() => InvokeCallAsync(service, serviceSetting, path, (client, endpoint) => client.DeleteAsync(endpoint)));
    }

    public async Task<T> Delete<T>(string service, string path)
        => await ParseResponse<T>(await Delete(service, path).ConfigureAwait(false)).ConfigureAwait(false);

    public Task<HttpResponseMessage> Get(string service, string path)
    {
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        return CreateRetryPolicy(options).ExecuteAsync(() => InvokeCallAsync(service, serviceSetting, path, (client, endpoint) => client.GetAsync(endpoint)));
    }

    public async Task<T> Get<T>(string service, string path)
        => await ParseResponse<T>(await Get(service, path).ConfigureAwait(false)).ConfigureAwait(false);

    public Task<HttpResponseMessage> Post(string service, string path, object payload)
        => ExecutePost(service, path, JsonSerializer.Serialize(payload, JsonOptions));

    public Task<HttpResponseMessage> Post(string service, string path, string payload)
        => ExecutePost(service, path, payload);

    public async Task<T> Post<T>(string service, string path, string payload)
        => await ParseResponse<T>(await Post(service, path, payload).ConfigureAwait(false)).ConfigureAwait(false);

    public async Task<T> Post<T>(string service, string path, object payload)
        => await ParseResponse<T>(await Post(service, path, payload).ConfigureAwait(false)).ConfigureAwait(false);

    private Task<HttpResponseMessage> ExecutePost(string service, string path, string content)
    {
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        return CreateRetryPolicy(options).ExecuteAsync(() => InvokeCallAsync(service, serviceSetting, path,
            (client, endpoint) => client.PostAsync(endpoint, new StringContent(content, Encoding.UTF8, "application/json"))));
    }

    public Task<HttpResponseMessage> Put(string service, string path, object payload)
    {
        var options = _optionsMonitor.CurrentValue;
        var serviceSetting = GetServiceSetting(service, options);
        return CreateRetryPolicy(options).ExecuteAsync(() => InvokeCallAsync(service, serviceSetting, path,
            (client, endpoint) => client.PutAsync(endpoint,
                new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"))));
    }

    public async Task<T> Put<T>(string service, string path, object payload)
        => await ParseResponse<T>(await Put(service, path, payload).ConfigureAwait(false)).ConfigureAwait(false);
}
