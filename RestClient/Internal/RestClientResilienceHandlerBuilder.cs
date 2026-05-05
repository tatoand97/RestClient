using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using NameProject.RestClient.Configurations;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using RestClientCircuitBreakerOptions = NameProject.RestClient.Configurations.CircuitBreakerStrategyOptions;
using RestClientRetryOptions = NameProject.RestClient.Configurations.RetryStrategyOptions;

namespace NameProject.RestClient.Internal;

internal static class RestClientResilienceHandlerBuilder
{
    private static readonly HashSet<HttpMethod> UnsafeMethods =
    [
        HttpMethod.Post,
        HttpMethod.Put,
        HttpMethod.Patch,
        HttpMethod.Delete,
        HttpMethod.Connect
    ];

    public static IHttpClientBuilder AddPresetResilienceHandler(
        this IHttpClientBuilder builder,
        string clientName,
        RestClientOptions options)
    {
        if (options.Resilience.Preset == ResiliencePreset.None)
        {
            return builder;
        }

        builder.AddResilienceHandler(clientName, (pipeline, context) =>
        {
            var logger = context.ServiceProvider
                .GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(RestClientResilienceHandlerBuilder).FullName!);
            ConfigurePipeline(clientName, options.Resilience, pipeline, logger);
        });
        return builder;
    }

    private static void ConfigurePipeline(
        string clientName,
        ResilienceOptions options,
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        ILogger? logger)
    {
        var hasRateLimiter = UsesRateLimiter(options.Preset);
        var hasTimeout = UsesTimeout(options.Preset);
        var hasRetry = UsesRetry(options.Preset);
        var hasCircuitBreaker = UsesCircuitBreaker(options.Preset);

        if (hasRateLimiter)
        {
            pipeline.AddConcurrencyLimiter(options.RateLimiter.PermitLimit, options.RateLimiter.QueueLimit);
        }

        if (hasTimeout)
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(options.Timeout.Seconds));
        }

        if (hasCircuitBreaker)
        {
            pipeline.AddCircuitBreaker(CreateCircuitBreakerOptions(options.CircuitBreaker));
        }

        if (hasRetry)
        {
            if (options.Preset == ResiliencePreset.TimeoutRetryTransient)
            {
                logger?.LogWarning(
                    "Client {ClientName} uses TimeoutRetryTransient. This preset can increase latency and should be used only for idempotent or explicitly approved operations.",
                    clientName);
            }

            pipeline.AddRetry(CreateRetryOptions(options));
        }

        if (hasRetry && HasAttemptTimeout(options.Preset))
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(options.Timeout.Seconds));
        }
    }

    private static RetryStrategyOptions<HttpResponseMessage> CreateRetryOptions(
        ResilienceOptions resilience)
    {
        var retry = resilience.Retry;
        var useBackoff = resilience.Preset is ResiliencePreset.BackoffRetryTransient or ResiliencePreset.TimeoutBackoffCircuitBreaker;

        var options = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = retry.Attempts,
            Delay = TimeSpan.FromSeconds(retry.BaseDelaySeconds),
            MaxDelay = TimeSpan.FromSeconds(retry.MaxDelaySeconds),
            BackoffType = useBackoff ? DelayBackoffType.Exponential : DelayBackoffType.Constant,
            UseJitter = useBackoff && retry.UseJitter,
            ShouldRetryAfterHeader = true,
            ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args, retry))
        };

        return options;
    }

    private static CircuitBreakerStrategyOptions<HttpResponseMessage> CreateCircuitBreakerOptions(
        RestClientCircuitBreakerOptions options)
        => new()
        {
            FailureRatio = options.FailureThresholdPercentage / 100.0,
            MinimumThroughput = options.MinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(options.SamplingDurationSeconds),
            BreakDuration = TimeSpan.FromSeconds(options.BreakDurationSeconds),
            ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome))
        };

    private static bool ShouldRetry(
        RetryPredicateArguments<HttpResponseMessage> args,
        RestClientRetryOptions options)
    {
        var request = args.Context.GetRequestMessage();
        if (request is not null && IsUnsafeMethod(request.Method) && !options.RetryUnsafeMethods)
        {
            return false;
        }

        if (args.Outcome.Exception is TimeoutRejectedException)
        {
            return options.HandleTimeouts;
        }

        if (args.Outcome.Exception is HttpRequestException)
        {
            return true;
        }

        var response = args.Outcome.Result;
        return response is not null && options.StatusCodes.Contains((int)response.StatusCode);
    }

    private static bool IsTransientFailure(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        var response = outcome.Result;
        if (response is null)
        {
            return false;
        }

        var statusCode = (int)response.StatusCode;
        return statusCode is 408 or 429 or 500 or 502 or 503 or 504;
    }

    private static bool IsUnsafeMethod(HttpMethod method)
        => UnsafeMethods.Contains(method);

    internal static bool UsesTimeout(ResiliencePreset preset)
        => preset is ResiliencePreset.TimeoutOnly
            or ResiliencePreset.TimeoutRetryTransient
            or ResiliencePreset.TimeoutCircuitBreaker
            or ResiliencePreset.TimeoutBackoffCircuitBreaker
            or ResiliencePreset.RateLimitedTimeout
            or ResiliencePreset.RateLimitedTimeoutCircuitBreaker;

    internal static bool UsesRetry(ResiliencePreset preset)
        => preset is ResiliencePreset.RetryTransient
            or ResiliencePreset.BackoffRetryTransient
            or ResiliencePreset.TimeoutRetryTransient
            or ResiliencePreset.RetryCircuitBreaker
            or ResiliencePreset.TimeoutBackoffCircuitBreaker;

    internal static bool UsesCircuitBreaker(ResiliencePreset preset)
        => preset is ResiliencePreset.TimeoutCircuitBreaker
            or ResiliencePreset.RetryCircuitBreaker
            or ResiliencePreset.TimeoutBackoffCircuitBreaker
            or ResiliencePreset.RateLimitedTimeoutCircuitBreaker;

    internal static bool UsesRateLimiter(ResiliencePreset preset)
        => preset is ResiliencePreset.RateLimitedTimeout
            or ResiliencePreset.RateLimitedTimeoutCircuitBreaker;

    private static bool HasAttemptTimeout(ResiliencePreset preset)
        => preset is ResiliencePreset.TimeoutRetryTransient
            or ResiliencePreset.TimeoutBackoffCircuitBreaker;
}
