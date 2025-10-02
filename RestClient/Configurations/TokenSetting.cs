using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;

namespace NameProject.RestClient.Configurations;

public class TokenSetting
{
    public const string DefaultGrantType = "client_credentials";
    public const string DefaultContentType = "application/x-www-form-urlencoded";

    public required Uri TokenUrl { get; set; }
    public AuthenticationType AuthenticationType { get; set; } = AuthenticationType.None;
    public string GrantType { get; set; } = DefaultGrantType;
    public string Scope { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public string ContentType { get; set; } = DefaultContentType;
    public Dictionary<string, string> DefaultRequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> GetTokenRequestBody()
    {
        var body = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["grant_type"] = GrantType
        };

        if (!string.IsNullOrWhiteSpace(Scope))
        {
            body["scope"] = Scope;
        }

        if (!string.IsNullOrWhiteSpace(Audience))
        {
            body["audience"] = Audience;
        }

        if (AuthenticationType == AuthenticationType.OAuth2Body)
        {
            body["client_id"] = ClientId;
            body["client_secret"] = ClientSecret;
        }

        return body;
    }

    public AuthenticationHeaderValue? GetClientAuthenticationHeader()
    {
        if (AuthenticationType != AuthenticationType.OAuth2Header)
        {
            return null;
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ClientId}:{ClientSecret}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
