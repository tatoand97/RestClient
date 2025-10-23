using NameProject.RestClient.Configurations;
using NameProject.RestClient.Handlers;

namespace NameProject.RestClient.ServiceDefinition;

/// <summary>
/// Configura los servicios del cliente REST a partir de la configuracion de la aplicacion.
/// </summary>
internal static class RestClientServiceConfigurator
{
    private const string ConfigurationSectionName = "RestClient";

    /// <summary>
    /// Registra los servicios del cliente REST, incluyendo instancias con nombre de <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="services">Coleccion de servicios donde se registran las dependencias.</param>
    /// <param name="configuration">Origen de la configuracion de la aplicacion.</param>
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

    /// <summary>
    /// Obtiene la configuracion del cliente REST desde la fuente de configuracion proporcionada.
    /// </summary>
    /// <param name="configuration">Origen de la configuracion de la aplicacion.</param>
    /// <returns>Modelo de configuracion normalizado para los servicios.</returns>
    /// <exception cref="InvalidOperationException">Se lanza cuando faltan secciones o valores obligatorios.</exception>
    private static ServiceConfigurationModel GetServiceConfiguration(IConfiguration configuration)
    {
        var restClientSection = GetRequiredSection(configuration, ConfigurationSectionName);
        var (httpClientRetry, httpClientDelay) = GetHttpClientOptions(restClientSection);
        var services = BuildServiceSettings(restClientSection);

        return new ServiceConfigurationModel(httpClientRetry, httpClientDelay, services);
    }

    /// <summary>
    /// Obtiene los valores de reintento y retardo configurados para los clientes HTTP.
    /// </summary>
    /// <param name="restClientSection">Seccion de configuracion del cliente REST.</param>
    /// <returns>Tupla con los valores de reintento y retardo.</returns>
    private static (int Retry, double Delay) GetHttpClientOptions(IConfigurationSection restClientSection)
    {
        var retry = restClientSection.GetValue<int>(nameof(RestClientOptions.HttpClientRetry));
        var delay = restClientSection.GetValue<double>(nameof(RestClientOptions.HttpClientDelay));
        return (retry, delay);
    }

    /// <summary>
    /// Construye el diccionario de servicios configurados a partir de la seccion correspondiente.
    /// </summary>
    /// <param name="restClientSection">Seccion de configuracion del cliente REST.</param>
    /// <returns>Diccionario con la configuracion de cada servicio.</returns>
    private static Dictionary<string, RestClientServiceSetting> BuildServiceSettings(IConfigurationSection restClientSection)
    {
        var servicesSection = GetRequiredSection(restClientSection, "Services");
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

        return services;
    }

    /// <summary>
    /// Obtiene una seccion obligatoria y verifica su existencia en la configuracion raiz.
    /// </summary>
    /// <param name="configuration">Configuracion raiz.</param>
    /// <param name="sectionName">Nombre de la seccion requerida.</param>
    /// <returns>La seccion existente solicitada.</returns>
    /// <exception cref="InvalidOperationException">Se lanza cuando la seccion no existe.</exception>
    private static IConfigurationSection GetRequiredSection(IConfiguration configuration, string sectionName)
    {
        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"Missing configuration section '{sectionName}'.");
        }

        return section;
    }

    /// <summary>
    /// Obtiene una seccion obligatoria y verifica su existencia dentro de otra seccion.
    /// </summary>
    /// <param name="parentSection">Seccion padre desde donde se busca.</param>
    /// <param name="sectionName">Nombre de la seccion requerida.</param>
    /// <returns>La seccion existente solicitada.</returns>
    /// <exception cref="InvalidOperationException">Se lanza cuando la seccion no existe.</exception>
    private static IConfigurationSection GetRequiredSection(IConfigurationSection parentSection, string sectionName)
    {
        var section = parentSection.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"Configuration section '{parentSection.Path}:{sectionName}' must be provided.");
        }

        return section;
    }

    /// <summary>
    /// Crea una instancia de <see cref="RestClientServiceSetting"/> validada a partir de la metadata de configuracion.
    /// </summary>
    /// <param name="definition">Datos originales de la definicion del servicio.</param>
    /// <param name="configurationPath">Ruta de configuracion utilizada para diagnostico.</param>
    /// <returns>Configuracion validada del servicio.</returns>
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

    /// <summary>
    /// Convierte la metadata de autenticacion en un <see cref="TokenSetting"/> cuando corresponde.
    /// </summary>
    /// <param name="authDefinition">Definicion de autenticacion extraida desde la configuracion.</param>
    /// <param name="keyPrefix">Ruta de configuracion utilizada para diagnostico.</param>
    /// <returns>Configuracion del token cuando la autenticacion esta habilitada; de lo contrario, <c>null</c>.</returns>
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

    /// <summary>
    /// Reemplaza los encabezados predeterminados del cliente HTTP con los valores configurados.
    /// </summary>
    /// <param name="client">Cliente cuyos encabezados seran actualizados.</param>
    /// <param name="headers">Pares clave-valor de encabezados a aplicar.</param>
    private static void ConfigureHeaders(HttpClient client, Dictionary<string, string> headers)
    {
        client.DefaultRequestHeaders.Clear();
        foreach (var header in headers)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }

    /// <summary>
    /// Garantiza que el valor de texto proporcionado no sea nulo ni espacios en blanco.
    /// </summary>
    /// <param name="value">Valor de configuracion a validar.</param>
    /// <param name="key">Clave de configuracion utilizada para diagnostico.</param>
    /// <returns>Valor de texto validado.</returns>
    /// <exception cref="InvalidOperationException">Se lanza cuando el valor esta ausente o vacio.</exception>
    private static string GetRequiredString(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be provided.");
        }

        return value;
    }

    /// <summary>
    /// Garantiza que el valor de tipo URI no sea nulo y sea absoluto.
    /// </summary>
    /// <param name="value">Valor de configuracion a validar.</param>
    /// <param name="key">Clave de configuracion utilizada para diagnostico.</param>
    /// <returns>URI absoluto validado.</returns>
    /// <exception cref="InvalidOperationException">Se lanza cuando el URI esta ausente o es relativo.</exception>
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
