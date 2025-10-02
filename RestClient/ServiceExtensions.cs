using NameProject.RestClient.Configurations;
using NameProject.RestClient.Interfaces;
using NameProject.RestClient.Services;

namespace NameProject.RestClient;

public static class ServiceExtensions
{
    public static void AddHttpClientWrapper(this IServiceCollection services, Action<RestClientOptions> optSetup)
    {
        services.AddOptions();
        services.Configure(optSetup);
        services.AddSingleton<IRestClientService, RestClientService>();
    }
}
