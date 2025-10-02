using NameProject.RestClient.Common;
using System.Text.Json.Serialization;

namespace NameProject.RestClient.Configurations;

public class TokenResponseDto
{
    [JsonPropertyName(Constants.AccessTokenKey)]
    public string? AccessToken { get; set; }

    [JsonPropertyName(Constants.ExpiresInKey)]
    public int ExpireIn { get; set; }

    [JsonPropertyName(Constants.TokenTypeKey)]
    public string? TokenType { get; set; }
}
