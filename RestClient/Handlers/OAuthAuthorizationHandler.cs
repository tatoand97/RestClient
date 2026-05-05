using System.Net;
using System.Net.Http.Headers;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Interfaces;

namespace NameProject.RestClient.Handlers;

public sealed class OAuthAuthorizationHandler(
    string clientName,
    AuthOptions options,
    IAccessTokenProvider accessTokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bufferedContent = await BufferContentAsync(request, cancellationToken).ConfigureAwait(false);

        var token = await accessTokenProvider.GetTokenAsync(clientName, options, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.Value);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        await accessTokenProvider.InvalidateAsync(clientName, cancellationToken).ConfigureAwait(false);

        var refreshedToken = await accessTokenProvider.GetTokenAsync(clientName, options, cancellationToken).ConfigureAwait(false);
        using var retryRequest = CloneRequest(request, bufferedContent);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue(refreshedToken.TokenType, refreshedToken.Value);

        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BufferedContent?> BufferContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return null;
        }

        var headers = request.Content.Headers
            .Select(header => new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray()))
            .ToArray();
        var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var originalContent = request.Content;

        request.Content = CreateContent(contentBytes, headers);
        originalContent.Dispose();

        return new BufferedContent(contentBytes, headers);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, BufferedContent? bufferedContent)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (bufferedContent is not null)
        {
            clone.Content = CreateContent(bufferedContent.Bytes, bufferedContent.Headers);
        }

        return clone;
    }

    private static ByteArrayContent CreateContent(byte[] bytes, KeyValuePair<string, string[]>[] headers)
    {
        var content = new ByteArrayContent(bytes);
        foreach (var header in headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return content;
    }

    private sealed record BufferedContent(byte[] Bytes, KeyValuePair<string, string[]>[] Headers);
}
