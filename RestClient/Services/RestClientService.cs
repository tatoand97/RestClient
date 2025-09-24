using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Exceptions;
using NameProject.RestClient.Interfaces;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NameProject.RestClient.Services
{
    public class RestClientService : IRestClientService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<RestClientService> _logger;
        private RestClientOptions _options;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public RestClientService(IHttpClientFactory clientFactory, IOptions<RestClientOptions> options, ILogger<RestClientService> logger)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _retryPolicy = HttpPolicyExtensions
                               .HandleTransientHttpError()
                               .WaitAndRetryAsync(
                                   Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(_options.HttpClientDelay), _options.HttpClientRetry),
                                   (result, timeSpan, retryCount, context) =>
                                   {
                                       _logger.LogWarning("Retry {RetryCount} after {TimeSpan} due to {StatusCode}", retryCount, timeSpan, result.Result?.StatusCode);
                                   });

            InitializeServices();
        }

        private void InitializeServices()
        {
            foreach (var service in _options.Services.Values)
            {
                service.Client = _clientFactory.CreateClient();
                ConfigureHeaders(service.Client, service.DefaultRequestHeaders);

                if (service.TokenRequest is not null)
                {
                    service.TokenRequest.Client = _clientFactory.CreateClient();
                    ConfigureHeaders(service.TokenRequest.Client, service.TokenRequest.DefaultRequestHeaders);
                }
            }
        }

        private static void ConfigureHeaders(HttpClient client, Dictionary<string, string> headers)
        {
            client.DefaultRequestHeaders.Clear();
            if (headers is not null)
            {
                foreach (var header in headers)
                    client.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }

        private static async Task<T> ParseResponse<T>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new RestRequestFailedException(response.StatusCode, content);

            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }

        private bool NeedsTokenRefresh(string serviceName)
        {
            return _options.Services.TryGetValue(serviceName, out var serviceSetting) &&
                   DateTime.UtcNow > serviceSetting.NextTimeUpdateToken;
        }

        private async Task UpdateTokenAsync(string serviceName)
        {
            if (!_options.Services.TryGetValue(serviceName, out var serviceSetting))
                throw new InvalidOperationException($"Service {serviceName} not configured.");

            if (string.IsNullOrEmpty(serviceSetting.TokenRequest?.Path))
                return;

            var token = await RequestTokenAsync(serviceName).ConfigureAwait(false);
            ConfigureHeaders(serviceSetting.Client, serviceSetting.DefaultRequestHeaders);
            serviceSetting.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
            serviceSetting.NextTimeUpdateToken = DateTime.UtcNow.AddSeconds(token.ExpireIn);
        }

        private async Task<TokenResponseDto> RequestTokenAsync(string serviceName)
        {
            if (!_options.Services.TryGetValue(serviceName, out var serviceSetting))
                throw new InvalidOperationException($"Service {serviceName} not configured.");

            var tokenRequestInfo = serviceSetting.TokenRequest ?? throw new InvalidOperationException($"Token request info not found for service {serviceName}.");
            var response = await tokenRequestInfo.Client.PostAsync(
                $"{tokenRequestInfo.BaseAddress}{tokenRequestInfo.Path}",
                new FormUrlEncodedContent(tokenRequestInfo.GetTokenRequestBody())).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogError("Error requesting token for {ServiceName}: {Error}", serviceName, errorContent);
                throw new ApiException("Error acquiring token");
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<TokenResponseDto>(content, JsonOptions);
        }

        private async Task<HttpResponseMessage> InvokeCallAsync(string service, string path, Func<string, Task<HttpResponseMessage>> call)
        {
            if (!_options.Services.TryGetValue(service, out var serviceSetting))
                throw new InvalidOperationException($"Service {service} not found.");

            try
            {
                if (serviceSetting.TokenRequest is not null && NeedsTokenRefresh(service) &&
                    serviceSetting.TokenRequest.Client.DefaultRequestHeaders.Authorization is null)
                {
                    await UpdateTokenAsync(service).ConfigureAwait(false);
                }

                var endpoint = $"{serviceSetting.BaseAddress}{path}";
                return await call(endpoint).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing http call for service {Service}, path {Path}", service, path);
                throw new RestRequestFailedException("Error executing HTTP call", ex);
            }
        }

        public Task<HttpResponseMessage> Delete(string service, string path)
            => _retryPolicy.ExecuteAsync(() => InvokeCallAsync(service, path, endpoint => _options.Services[service].Client.DeleteAsync(endpoint)));

        public async Task<T> Delete<T>(string service, string path)
            => await ParseResponse<T>(await Delete(service, path).ConfigureAwait(false)).ConfigureAwait(false);

        public Task<HttpResponseMessage> Get(string service, string path)
            => _retryPolicy.ExecuteAsync(() => InvokeCallAsync(service, path, endpoint => _options.Services[service].Client.GetAsync(endpoint)));

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
            => _retryPolicy.ExecuteAsync(() => InvokeCallAsync(service, path,
                endpoint => _options.Services[service].Client.PostAsync(endpoint, new StringContent(content, Encoding.UTF8, "application/json"))));


        public Task<HttpResponseMessage> Put(string service, string path, object payload)
            => _retryPolicy.ExecuteAsync(() => InvokeCallAsync(service, path,
                endpoint => _options.Services[service].Client.PutAsync(endpoint,
                    new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"))));

        public async Task<T> Put<T>(string service, string path, object payload)
            => await ParseResponse<T>(await Put(service, path, payload).ConfigureAwait(false)).ConfigureAwait(false);

        public void RefreshConfigurationOptions(RestClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            InitializeServices();
        }
    }
}