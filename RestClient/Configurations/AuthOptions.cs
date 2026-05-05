namespace NameProject.RestClient.Configurations;

public sealed class AuthOptions
{
    public AuthenticationType Type { get; set; } = AuthenticationType.None;
    public Uri? TokenUrl { get; set; }
    public string GrantType { get; set; } = "client_credentials";
    public string Scope { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string ContentType { get; set; } = "application/x-www-form-urlencoded";
    public bool SendRequestBody { get; set; } = true;
    public Dictionary<string, string> DefaultRequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
