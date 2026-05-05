namespace NameProject.RestClient.Configurations;

public sealed class ResilienceOptions
{
    public ResiliencePreset Preset { get; set; } = ResiliencePreset.TimeoutOnly;
    public bool StrictPresetValidation { get; set; }
    public TimeoutStrategyOptions Timeout { get; set; } = new();
    public RetryStrategyOptions Retry { get; set; } = new();
    public CircuitBreakerStrategyOptions CircuitBreaker { get; set; } = new();
    public RateLimiterStrategyOptions RateLimiter { get; set; } = new();
}

public enum ResiliencePreset
{
    None,
    TimeoutOnly,
    RetryTransient,
    BackoffRetryTransient,
    TimeoutRetryTransient,
    TimeoutCircuitBreaker,
    RetryCircuitBreaker,
    TimeoutBackoffCircuitBreaker,
    RateLimitedTimeout,
    RateLimitedTimeoutCircuitBreaker
}

public sealed class TimeoutStrategyOptions
{
    public int Seconds { get; set; } = 30;
}

public sealed class RetryStrategyOptions
{
    public int Attempts { get; set; } = 2;
    public double BaseDelaySeconds { get; set; } = 1;
    public double MaxDelaySeconds { get; set; } = 10;
    public bool UseJitter { get; set; } = true;
    public bool HandleTimeouts { get; set; }
    public bool RetryUnsafeMethods { get; set; }
    public HashSet<int> StatusCodes { get; set; } = [408, 429, 500, 502, 503, 504];
}

public sealed class CircuitBreakerStrategyOptions
{
    public int FailureThresholdPercentage { get; set; } = 50;
    public int MinimumThroughput { get; set; } = 20;
    public int SamplingDurationSeconds { get; set; } = 30;
    public int BreakDurationSeconds { get; set; } = 30;
}

public sealed class RateLimiterStrategyOptions
{
    public int PermitLimit { get; set; } = 100;
    public int QueueLimit { get; set; }
}
