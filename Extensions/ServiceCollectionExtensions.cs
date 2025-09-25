using Azure.Identity;
using NameProject.RestClient;
using NameProject.RestClient.Common;
using NameProject.RestClient.Configurations;

namespace RestClient.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHttpClientConfiguration(this IServiceCollection services)
    {
        var serviceConfiguration = GetServiceConfiguration(services);

        services.AddHttpClient();
        services.AddHttpClientWrapper(options =>
        {
            Dictionary<string, RestClientServiceSetting> restClientServices = [];
            options.Services = restClientServices;

            options.HttpClientRetry = serviceConfiguration.HttpClientRetry;
            options.HttpClientDelay = serviceConfiguration.HttpClientDelay;

            TokenSetting setting = new()
            {
                Path = serviceConfiguration.TokenSetting.Path,
                GrantType = serviceConfiguration.TokenSetting.GrantType,
                ClientId = serviceConfiguration.TokenSetting.ClientId,
                Scope = serviceConfiguration.TokenSetting.Scope,
                ClientSecret = serviceConfiguration.TokenSetting.ClientSecret,
                ContentType = serviceConfiguration.TokenSetting.ContentType,
                DefaultRequestHeaders = new Dictionary<string, string>(serviceConfiguration.TokenSetting.DefaultRequestHeaders),
                BaseAddress = serviceConfiguration.TokenSetting.BaseAddress
            };

            options.Services.Add(Constants.TUYASERVICEKEYINGRESS, new RestClientServiceSetting()
            {
                BaseAddress = (serviceConfiguration.TuyaIngressService),
            });

            options.Services.Add(Constants.TUYASERVICEKEY, new RestClientServiceSetting()
            {
                BaseAddress = (serviceConfiguration.TuyaService),
                DefaultRequestHeaders = setting.DefaultRequestHeaders,
                TokenRequest = setting
            });
        });
        

        return services;
    }
}
