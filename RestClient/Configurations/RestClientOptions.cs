namespace NameProject.RestClient.Configurations;

public sealed class RestClientOptions
{
    public required Uri BaseAddress { get; set; }
    public Dictionary<string, string> DefaultRequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TimeoutSeconds { get; set; } = 100;
    public RetryOptions Retry { get; set; } = new();
    public AuthOptions? Auth { get; set; }
}
