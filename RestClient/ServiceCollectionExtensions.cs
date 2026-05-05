using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Handlers;
using NameProject.RestClient.Interfaces;
using NameProject.RestClient.Services;
using Polly;
using Polly.Extensions.Http;

namespace NameProject.RestClient;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyRestClient<TClient, TImplementation>(
        this IServiceCollection services,
        IConfigurationSection section)
        where TClient : class
        where TImplementation : class, TClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        var clientName = GetClientName(section);
        var options = BindAndValidateOptions(clientName, section);

        services.TryAddSingleton<IRestClientSerializer, DefaultRestClientSerializer>();
        services.TryAddSingleton<IHttpErrorHandler, DefaultHttpErrorHandler>();
        services.TryAddSingleton<IAccessTokenProvider, DefaultAccessTokenProvider>();

        var clientBuilder = services.AddHttpClient(clientName, client =>
        {
            client.BaseAddress = options.BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            ConfigureHeaders(client, options.DefaultRequestHeaders);
        });

        if (options.Auth is { Type: not AuthenticationType.None } authOptions)
        {
            ValidateAuthOptions(clientName, authOptions);
            clientBuilder.AddHttpMessageHandler(sp =>
                ActivatorUtilities.CreateInstance<OAuthAuthorizationHandler>(sp, clientName, authOptions));
        }

        clientBuilder.AddPolicyHandler(CreateRetryPolicy(options.Retry));

        services.AddTransient<TClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(clientName);

            var companyClient = new CompanyRestClient(
                httpClient,
                sp.GetRequiredService<IRestClientSerializer>(),
                sp.GetRequiredService<IHttpErrorHandler>(),
                sp.GetRequiredService<ILogger<CompanyRestClient>>());

            return ActivatorUtilities.CreateInstance<TImplementation>(sp, companyClient);
        });

        return services;
    }

    private static string GetClientName(IConfigurationSection section)
    {
        if (!string.IsNullOrWhiteSpace(section.Key))
        {
            return section.Key;
        }

        if (!string.IsNullOrWhiteSpace(section.Path))
        {
            return section.Path;
        }

        throw new InvalidOperationException("The HTTP client configuration section must have a key.");
    }

    private static RestClientOptions BindAndValidateOptions(string clientName, IConfigurationSection section)
    {
        var options = section.Get<RestClientOptions>()
                      ?? throw new InvalidOperationException($"Configuration section '{section.Path}' could not be bound.");

        if (options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Client {clientName} must define an absolute BaseAddress.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define TimeoutSeconds greater than zero.");
        }

        if (options.Retry.Attempts < 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Retry:Attempts greater than or equal to zero.");
        }

        if (options.Retry.BaseDelaySeconds <= 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Retry:BaseDelaySeconds greater than zero.");
        }

        return options;
    }

    private static void ValidateAuthOptions(string clientName, AuthOptions options)
    {
        if (options.TokenUrl is null || !options.TokenUrl.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Client {clientName} must define an absolute Auth:TokenUrl.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException($"Client {clientName} must define Auth:ClientId.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException($"Client {clientName} must define Auth:ClientSecret.");
        }
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(RetryOptions retry)
    {
        if (retry.Attempts == 0)
        {
            return Policy.NoOpAsync<HttpResponseMessage>();
        }

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retry.Attempts,
                attempt => TimeSpan.FromSeconds(retry.BaseDelaySeconds * Math.Pow(2, attempt - 1)));
    }

    private static void ConfigureHeaders(HttpClient client, Dictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
