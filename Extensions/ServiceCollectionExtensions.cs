using NameProject.RestClient.ServiceDefinition;

namespace RestClient.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHttpClientConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        RestClientServiceConfigurator.Configure(services, configuration);
        return services;
    }
}
