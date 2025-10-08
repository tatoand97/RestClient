namespace NameProject.RestClient.ServiceDefinition;

internal sealed class ServiceDefinition
{
    public string? Name { get; set; }
    public Uri? BaseUrl { get; set; }
    public Dictionary<string, string>? DefaultRequestHeaders { get; set; }
    public AuthDefinition? Auth { get; set; }
}
