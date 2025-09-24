using System;
using System.Net.Http.Headers;

namespace NameProject.RestClient.Configurations;

public class RestClientServiceSetting : BaseRestClientSetting
{
    public DateTime NextTimeUpdateToken { get; set; }
    public TokenSetting TokenRequest { get; set; }
    public AuthenticationHeaderValue AuthenticationHeaderValue { get; set; }
}
