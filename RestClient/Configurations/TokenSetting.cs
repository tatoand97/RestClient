using System.Collections.Generic;

namespace NameProject.RestClient.Configurations;

public class TokenSetting : BaseRestClientSetting
{
    public required string Path { get; set; }
    public required string GrantType { get; set; }
    public required string ClientId { get; set; }
    public required string Scope { get; set; }
    public required string ClientSecret { get; set; }
    public string ContentType { get; set; } = "application/x-www-form-urlencoded";

    public Dictionary<string, string> GetTokenRequestBody() => new()
    {
        { "client_id", ClientId },
        { "client_secret", ClientSecret },
        { "grant_type", GrantType },
        { "scope", Scope },
    };
}
