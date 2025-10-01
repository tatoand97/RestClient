using Microsoft.Extensions.Options;
using NameProject.RestClient.Common;
using NameProject.RestClient.Configurations;

namespace NameProject.RestClient.Handlers;

public class TuyaOAuthHandler : OAuthTokenHandler
{
    public TuyaOAuthHandler(
        IOptionsMonitor<RestClientOptions> optionsMonitor,
        IHttpClientFactory httpClientFactory,
        ILogger<OAuthTokenHandler> logger)
        : base(Constants.TUYASERVICEKEY, optionsMonitor, httpClientFactory, logger)
    {
    }
}
