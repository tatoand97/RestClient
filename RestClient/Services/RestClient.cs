using System.Text;
using System.Net;
using Microsoft.Extensions.Logging;
using NameProject.RestClient.Interfaces;

namespace NameProject.RestClient.Services;

public sealed class RestClient(
    HttpClient httpClient,
    IRestClientSerializer serializer,
    IHttpErrorHandler errorHandler,
    ILogger<RestClient> logger) : IRestClient
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IRestClientSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly IHttpErrorHandler _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
    private readonly ILogger<RestClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await _errorHandler.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error executing HTTP {Method} {Uri}", request.Method, request.RequestUri);
            throw;
        }
    }

    public async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await DeserializeResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResponse> PostAsync<TResponse>(string path, object payload, CancellationToken cancellationToken = default)
    {
        using var response = await PostAsync(path, payload, cancellationToken).ConfigureAwait(false);
        return await DeserializeResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResponse> PutAsync<TResponse>(string path, object payload, CancellationToken cancellationToken = default)
    {
        using var response = await PutAsync(path, payload, cancellationToken).ConfigureAwait(false);
        return await DeserializeResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        using var response = await DeleteRawAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    public Task<HttpResponseMessage> PostAsync(string path, object payload, CancellationToken cancellationToken = default)
        => SendAsync(CreateJsonRequest(HttpMethod.Post, path, payload), cancellationToken);

    public Task<HttpResponseMessage> PutAsync(string path, object payload, CancellationToken cancellationToken = default)
        => SendAsync(CreateJsonRequest(HttpMethod.Put, path, payload), cancellationToken);

    public Task<HttpResponseMessage> DeleteRawAsync(string path, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, path), cancellationToken);

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object payload)
    {
        var content = _serializer.Serialize(payload);
        return new HttpRequestMessage(method, path)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private async Task<TResponse> DeserializeResponseAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            ThrowEmptyTypedResponse(response);
        }

        var content = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(content))
        {
            ThrowEmptyTypedResponse(response);
        }

        return _serializer.Deserialize<TResponse>(content);
    }

    private static void ThrowEmptyTypedResponse(HttpResponseMessage response)
    {
        var request = response.RequestMessage;
        throw new InvalidOperationException(
            $"HTTP request {request?.Method.Method ?? "UNKNOWN"} {request?.RequestUri?.ToString() ?? "unknown"} succeeded with status code {(int)response.StatusCode}, but the response body was empty. Typed methods require a JSON response body. No-content endpoints should use SendAsync, raw methods, DeleteAsync, or a dedicated no-content method.");
    }
}
