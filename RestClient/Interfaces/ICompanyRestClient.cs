namespace NameProject.RestClient.Interfaces;

public interface ICompanyRestClient
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default);
    Task<TResponse> PostAsync<TResponse>(string path, object payload, CancellationToken cancellationToken = default);
    Task<TResponse> PutAsync<TResponse>(string path, object payload, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> PostAsync(string path, object payload, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> PutAsync(string path, object payload, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> DeleteRawAsync(string path, CancellationToken cancellationToken = default);
}
