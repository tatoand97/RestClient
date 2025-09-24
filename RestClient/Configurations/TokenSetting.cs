using System.Collections.Generic;

namespace NameProject.RestClient.Configurations;

public class TokenSetting : BaseRestClientSetting
{
    public string Path { get; set; }
    public string GrantType { get; set; }
    public string ClientId { get; set; }
    public string Scope { get; set; }
    public string ClientSecret { get; set; }
    public string ContentType { get; set; }

    public Dictionary<string, string> GetTokenRequestBody()
    {
        return new Dictionary<string, string>
        {
            {"client_id", ClientId},
            {"client_secret", ClientSecret},
            {"grant_type", GrantType},
            {"scope", Scope},
        };
    }

}
