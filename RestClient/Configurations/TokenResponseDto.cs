using NameProject.RestClient.Common;
using System.Text.Json.Serialization;

namespace NameProject.RestClient.Configurations;

public class TokenResponseDto
{
    [JsonPropertyName(Constants.ACCESSTOKEN)]
    public string AccessToken { get; set; }
    [JsonPropertyName(Constants.EXPIREIN)]
    public int ExpireIn { get; set; }
    [JsonPropertyName(Constants.TOKENTYPE)]
    public string TokenType { get; set; }
}
