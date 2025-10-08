using NameProject.RestClient.Configurations;

namespace NameProject.RestClient.ServiceDefinition;

internal sealed class AuthDefinition
{
    public AuthenticationType Type { get; set; } = AuthenticationType.None;
    public Uri? TokenUrl { get; set; }
    public string? GrantType { get; set; }
    public string? Scope { get; set; }
    public string? Audience { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public bool? SendRequestBody { get; set; }
    public Dictionary<string, string>? DefaultRequestHeaders { get; set; }
    public string? ContentType { get; set; }
}
