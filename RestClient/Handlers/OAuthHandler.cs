using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NameProject.RestClient.Common;
using NameProject.RestClient.Configurations;

namespace NameProject.RestClient.Handlers;

public class OAuthHandler : OAuthTokenHandler
{
    public OAuthHandler(
        IOptionsMonitor<RestClientOptions> optionsMonitor,
        IHttpClientFactory httpClientFactory,
        ILogger<OAuthTokenHandler> logger)
        : this(Constants.ClientName, optionsMonitor, httpClientFactory, logger)
    {
    }

    public OAuthHandler(
        string serviceName,
        IOptionsMonitor<RestClientOptions> optionsMonitor,
        IHttpClientFactory httpClientFactory,
        ILogger<OAuthTokenHandler> logger)
        : base(serviceName, optionsMonitor, httpClientFactory, logger)
    {
    }
}
