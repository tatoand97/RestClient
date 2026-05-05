using System.Net;

namespace NameProject.RestClient.Exceptions;

public sealed class ExternalApiException(
    HttpStatusCode statusCode,
    string? reasonPhrase,
    Uri? requestUri,
    HttpMethod? method,
    string? responseBody,
    string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ReasonPhrase { get; } = reasonPhrase;
    public Uri? RequestUri { get; } = requestUri;
    public HttpMethod? Method { get; } = method;
    public string? ResponseBody { get; } = responseBody;
}
