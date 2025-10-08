using NameProject.RestClient;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Handlers;

namespace NameProject.RestClient.ServiceDefinition;

internal static class RestClientServiceConfigurator
{
    private const string ConfigurationSectionName = "RestClient";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        var serviceConfiguration = GetServiceConfiguration(configuration);

        services.AddHttpClient();
        services.AddHttpClientWrapper(options =>
        {
            options.HttpClientRetry = serviceConfiguration.HttpClientRetry;
            options.HttpClientDelay = serviceConfiguration.HttpClientDelay;
            options.Services = new Dictionary<string, RestClientServiceSetting>(serviceConfiguration.Services, StringComparer.OrdinalIgnoreCase);
        });

        foreach (var (serviceName, serviceSetting) in serviceConfiguration.Services)
        {
            var clientBuilder = services.AddHttpClient(serviceName, client =>
            {
                client.BaseAddress = serviceSetting.BaseAddress;
                ConfigureHeaders(client, serviceSetting.DefaultRequestHeaders);
            });

            if (serviceSetting.TokenRequest is not null)
            {
                clientBuilder.AddHttpMessageHandler(sp =>
                    ActivatorUtilities.CreateInstance<OAuthHandler>(sp, serviceName));
            }
        }
    }

    private static ServiceConfigurationModel GetServiceConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"Missing configuration section '{ConfigurationSectionName}'.");
        }

        var httpClientRetry = section.GetValue<int>(nameof(RestClientOptions.HttpClientRetry));
        var httpClientDelay = section.GetValue<double>(nameof(RestClientOptions.HttpClientDelay));

        var servicesSection = section.GetSection("Services");
        if (!servicesSection.Exists())
        {
            throw new InvalidOperationException($"Configuration section '{ConfigurationSectionName}:Services' must be provided.");
        }

        var services = new Dictionary<string, RestClientServiceSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var serviceSection in servicesSection.GetChildren())
        {
            var descriptor = serviceSection.Get<ServiceDefinition>()
                             ?? throw new InvalidOperationException($"Configuration for '{serviceSection.Path}' could not be bound.");
            var serviceName = GetRequiredString(descriptor.Name, $"{serviceSection.Path}:Name");
            if (services.ContainsKey(serviceName))
            {
                throw new InvalidOperationException($"Service '{serviceName}' is configured more than once.");
            }

            var serviceSetting = CreateServiceSetting(descriptor, serviceSection.Path);
            services.Add(serviceName, serviceSetting);
        }

        if (services.Count == 0)
        {
            throw new InvalidOperationException($"Configuration section '{ConfigurationSectionName}:Services' must define at least one service.");
        }

        return new ServiceConfigurationModel(httpClientRetry, httpClientDelay, services);
    }

    private static RestClientServiceSetting CreateServiceSetting(ServiceDefinition definition, string configurationPath)
    {
        var baseAddress = GetRequiredUri(definition.BaseUrl, $"{configurationPath}:BaseUrl");
        var headers = definition.DefaultRequestHeaders is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(definition.DefaultRequestHeaders, StringComparer.OrdinalIgnoreCase);

        TokenSetting? tokenSetting = null;
        if (definition.Auth is not null)
        {
            tokenSetting = NormalizeTokenSetting(definition.Auth, $"{configurationPath}:Auth");
        }

        return new RestClientServiceSetting
        {
            BaseAddress = baseAddress,
            DefaultRequestHeaders = headers,
            TokenRequest = tokenSetting
        };
    }

    private static TokenSetting? NormalizeTokenSetting(AuthDefinition authDefinition, string keyPrefix)
    {
        if (authDefinition.Type == AuthenticationType.None)
        {
            return null;
        }

        var tokenUrl = GetRequiredUri(authDefinition.TokenUrl, $"{keyPrefix}:TokenUrl");
        var clientId = GetRequiredString(authDefinition.ClientId, $"{keyPrefix}:ClientId");
        var clientSecret = GetRequiredString(authDefinition.ClientSecret, $"{keyPrefix}:ClientSecret");
        var scope = GetRequiredString(authDefinition.Scope, $"{keyPrefix}:Scope");
        var audience = GetRequiredString(authDefinition.Audience, $"{keyPrefix}:Audience");
        var grantType = string.IsNullOrWhiteSpace(authDefinition.GrantType)
            ? TokenSetting.DefaultGrantType
            : authDefinition.GrantType!;

        var headers = authDefinition.DefaultRequestHeaders is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(authDefinition.DefaultRequestHeaders, StringComparer.OrdinalIgnoreCase);

        if (authDefinition.Type == AuthenticationType.OAuth2Header && headers.ContainsKey("Authorization"))
        {
            headers.Remove("Authorization");
        }

        return new TokenSetting
        {
            TokenUrl = tokenUrl,
            AuthenticationType = authDefinition.Type,
            ClientId = clientId,
            ClientSecret = clientSecret,
            GrantType = grantType,
            Scope = scope,
            Audience = audience,
            SendRequestBody = authDefinition.SendRequestBody ?? true,
            ContentType = string.IsNullOrWhiteSpace(authDefinition.ContentType)
                ? TokenSetting.DefaultContentType
                : authDefinition.ContentType!,
            DefaultRequestHeaders = headers
        };
    }

    private static void ConfigureHeaders(HttpClient client, Dictionary<string, string> headers)
    {
        client.DefaultRequestHeaders.Clear();
        foreach (var header in headers)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }

    private static string GetRequiredString(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be provided.");
        }

        return value;
    }

    private static Uri GetRequiredUri(Uri? value, string key)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be provided.");
        }

        if (!value.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be a valid absolute URI.");
        }

        return value;
    }
}
