using ApimIdenty.Options;
using ApimIdenty.Services;
using Azure.Core;
using Azure.Identity;


namespace ApimIdenty.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddOptions<ApimOptions>()
            .Bind(configuration.GetSection("Apim"))
            .ValidateDataAnnotations()
            .Validate(
                options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "Apim:BaseUrl configuration must be a valid absolute URI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Scope),
                "Apim:Scope configuration is required.");

        services.AddSingleton<TokenCredential, DefaultAzureCredential>();
        services.AddTransient<BearerTokenHandler>();

        services.AddHttpClient<ApimClient>()
            .AddHttpMessageHandler<BearerTokenHandler>();

        return services;
    }

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

        //services.AddTransient<IClienteinformacionServices, ClienteinformacionServices>();

        return services;
    }
}
