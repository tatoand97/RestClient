using System.Text;
using Microsoft.Extensions.Logging;
using NameProject.RestClient.Exceptions;
using NameProject.RestClient.Interfaces;

namespace NameProject.RestClient.Services;

public sealed class DefaultHttpErrorHandler(ILogger<DefaultHttpErrorHandler> logger) : IHttpErrorHandler
{
    internal const int MaxCapturedBodyLength = 2048;

    public async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var truncatedBody = Truncate(body);
        var request = response.RequestMessage;
        var method = request?.Method;
        var uri = request?.RequestUri;
        var reason = response.ReasonPhrase;

        logger.LogError(
            "HTTP {Method} {Uri} failed with status code {StatusCode} ({ReasonPhrase}). Response body: {ResponseBody}",
            method?.Method ?? "UNKNOWN",
            uri,
            (int)response.StatusCode,
            reason,
            truncatedBody);

        var message = new StringBuilder()
            .Append("External API request failed with status code ")
            .Append((int)response.StatusCode)
            .Append(" (")
            .Append(reason ?? "No reason phrase")
            .Append("). Method: ")
            .Append(method?.Method ?? "UNKNOWN")
            .Append(". Uri: ")
            .Append(uri?.ToString() ?? "unknown")
            .ToString();

        throw new ExternalApiException(response.StatusCode, reason, uri, method, truncatedBody, message);
    }

    internal static string Truncate(string value)
        => string.IsNullOrEmpty(value) || value.Length <= MaxCapturedBodyLength
            ? value
            : string.Concat(value.AsSpan(0, MaxCapturedBodyLength), "...(truncated)");
}
