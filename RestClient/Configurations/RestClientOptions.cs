using System.Collections.Generic;

namespace NameProject.RestClient.Configurations;

public class RestClientOptions
{
    public Dictionary<string, RestClientServiceSetting> Services { get; set; }
    public double HttpClientDelay { get; set; }
    public int HttpClientRetry { get; set; }

}
