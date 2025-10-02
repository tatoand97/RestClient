namespace NameProject.RestClient.Configurations;

public class RestClientOptions
{
    public Dictionary<string, RestClientServiceSetting> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double HttpClientDelay { get; set; }
    public int HttpClientRetry { get; set; }
}
