using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Handlers;
using NameProject.RestClient.Interfaces;
using NameProject.RestClient.Internal;
using NameProject.RestClient.Services;

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

        clientBuilder.AddPresetResilienceHandler(clientName, options);

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

        ValidateResilienceOptions(clientName, section, options);

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

    private static void ValidateResilienceOptions(string clientName, IConfigurationSection section, RestClientOptions options)
    {
        var resilience = options.Resilience;
        var resilienceSection = section.GetSection(nameof(RestClientOptions.Resilience));
        var preset = resilience.Preset;

        if (RestClientResilienceHandlerBuilder.UsesTimeout(preset))
        {
            if (resilience.Timeout.Seconds <= 0)
            {
                throw new InvalidOperationException($"Client {clientName} must define Resilience:Timeout:Seconds greater than zero.");
            }

            if (resilience.Timeout.Seconds > options.TimeoutSeconds)
            {
                throw new InvalidOperationException($"Client {clientName} must define Resilience:Timeout:Seconds less than or equal to TimeoutSeconds.");
            }
        }

        if (RestClientResilienceHandlerBuilder.UsesRetry(preset))
        {
            ValidateRetryOptions(clientName, resilience.Retry);
        }

        if (RestClientResilienceHandlerBuilder.UsesCircuitBreaker(preset))
        {
            ValidateCircuitBreakerOptions(clientName, resilience.CircuitBreaker);
        }

        if (RestClientResilienceHandlerBuilder.UsesRateLimiter(preset))
        {
            ValidateRateLimiterOptions(clientName, resilience.RateLimiter);
        }

        ValidatePresetCompatibility(clientName, resilienceSection, resilience);
    }

    private static void ValidateRetryOptions(string clientName, RetryStrategyOptions options)
    {
        if (options.Attempts < 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:Retry:Attempts greater than or equal to zero.");
        }

        if (options.BaseDelaySeconds <= 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:Retry:BaseDelaySeconds greater than zero.");
        }

        if (options.MaxDelaySeconds <= 0 || options.MaxDelaySeconds < options.BaseDelaySeconds)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:Retry:MaxDelaySeconds greater than or equal to BaseDelaySeconds.");
        }

        if (options.StatusCodes.Count == 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define at least one Resilience:Retry:StatusCodes value.");
        }
    }

    private static void ValidateCircuitBreakerOptions(string clientName, CircuitBreakerStrategyOptions options)
    {
        if (options.FailureThresholdPercentage <= 0 || options.FailureThresholdPercentage > 100)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:CircuitBreaker:FailureThresholdPercentage between 1 and 100.");
        }

        if (options.MinimumThroughput <= 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:CircuitBreaker:MinimumThroughput greater than zero.");
        }

        if (options.SamplingDurationSeconds <= 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:CircuitBreaker:SamplingDurationSeconds greater than zero.");
        }

        if (options.BreakDurationSeconds <= 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:CircuitBreaker:BreakDurationSeconds greater than zero.");
        }
    }

    private static void ValidateRateLimiterOptions(string clientName, RateLimiterStrategyOptions options)
    {
        if (options.PermitLimit <= 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:RateLimiter:PermitLimit greater than zero.");
        }

        if (options.QueueLimit < 0)
        {
            throw new InvalidOperationException($"Client {clientName} must define Resilience:RateLimiter:QueueLimit greater than or equal to zero.");
        }
    }

    private static void ValidatePresetCompatibility(string clientName, IConfigurationSection section, ResilienceOptions options)
    {
        ValidateStrategyCompatibility(
            clientName,
            section.GetSection(nameof(ResilienceOptions.Timeout)),
            nameof(ResilienceOptions.Timeout),
            RestClientResilienceHandlerBuilder.UsesTimeout(options.Preset),
            options.StrictPresetValidation,
            IsDefaultTimeout(options.Timeout));

        ValidateStrategyCompatibility(
            clientName,
            section.GetSection(nameof(ResilienceOptions.Retry)),
            nameof(ResilienceOptions.Retry),
            RestClientResilienceHandlerBuilder.UsesRetry(options.Preset),
            options.StrictPresetValidation,
            IsDefaultRetry(options.Retry));

        ValidateStrategyCompatibility(
            clientName,
            section.GetSection(nameof(ResilienceOptions.CircuitBreaker)),
            nameof(ResilienceOptions.CircuitBreaker),
            RestClientResilienceHandlerBuilder.UsesCircuitBreaker(options.Preset),
            options.StrictPresetValidation,
            IsDefaultCircuitBreaker(options.CircuitBreaker));

        ValidateStrategyCompatibility(
            clientName,
            section.GetSection(nameof(ResilienceOptions.RateLimiter)),
            nameof(ResilienceOptions.RateLimiter),
            RestClientResilienceHandlerBuilder.UsesRateLimiter(options.Preset),
            options.StrictPresetValidation,
            IsDefaultRateLimiter(options.RateLimiter));
    }

    private static void ValidateStrategyCompatibility(
        string clientName,
        IConfigurationSection section,
        string strategyName,
        bool isRelevant,
        bool strictPresetValidation,
        bool hasDefaultValues)
    {
        if (isRelevant || !section.Exists())
        {
            return;
        }

        if (strictPresetValidation || !hasDefaultValues)
        {
            throw new InvalidOperationException(
                $"Client {clientName} uses a resilience preset that does not support Resilience:{strategyName} options.");
        }
    }

    private static bool IsDefaultTimeout(TimeoutStrategyOptions options)
    {
        var defaults = new TimeoutStrategyOptions();
        return options.Seconds == defaults.Seconds;
    }

    private static bool IsDefaultRetry(RetryStrategyOptions options)
    {
        var defaults = new RetryStrategyOptions();
        return options.Attempts == defaults.Attempts
               && options.BaseDelaySeconds.Equals(defaults.BaseDelaySeconds)
               && options.MaxDelaySeconds.Equals(defaults.MaxDelaySeconds)
               && options.UseJitter == defaults.UseJitter
               && options.HandleTimeouts == defaults.HandleTimeouts
               && options.RetryUnsafeMethods == defaults.RetryUnsafeMethods
               && options.StatusCodes.SetEquals(defaults.StatusCodes);
    }

    private static bool IsDefaultCircuitBreaker(CircuitBreakerStrategyOptions options)
    {
        var defaults = new CircuitBreakerStrategyOptions();
        return options.FailureThresholdPercentage == defaults.FailureThresholdPercentage
               && options.MinimumThroughput == defaults.MinimumThroughput
               && options.SamplingDurationSeconds == defaults.SamplingDurationSeconds
               && options.BreakDurationSeconds == defaults.BreakDurationSeconds;
    }

    private static bool IsDefaultRateLimiter(RateLimiterStrategyOptions options)
    {
        var defaults = new RateLimiterStrategyOptions();
        return options.PermitLimit == defaults.PermitLimit
               && options.QueueLimit == defaults.QueueLimit;
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
