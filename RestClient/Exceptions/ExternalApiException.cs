using System.Net;

namespace NameProject.RestClient.Exceptions;

public sealed class ExternalApiException : Exception
{
    public ExternalApiException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        Uri? requestUri,
        HttpMethod? method,
        string? responseBody,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        RequestUri = requestUri;
        Method = method;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ReasonPhrase { get; }
    public Uri? RequestUri { get; }
    public HttpMethod? Method { get; }
    public string? ResponseBody { get; }
}
