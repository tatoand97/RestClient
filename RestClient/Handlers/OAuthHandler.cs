using Microsoft.Extensions.Options;
using NameProject.RestClient.Common;
using NameProject.RestClient.Configurations;

namespace NameProject.RestClient.Handlers;

public class OAuthHandler(
    string serviceName,
    IOptionsMonitor<RestClientOptions> optionsMonitor,
    IHttpClientFactory httpClientFactory,
    ILogger<OAuthTokenHandler> logger) : OAuthTokenHandler(serviceName, optionsMonitor, httpClientFactory, logger)
{
    public OAuthHandler(
        IOptionsMonitor<RestClientOptions> optionsMonitor,
        IHttpClientFactory httpClientFactory,
        ILogger<OAuthTokenHandler> logger)
        : this(Constants.ClientName, optionsMonitor, httpClientFactory, logger)
    {
    }
}
