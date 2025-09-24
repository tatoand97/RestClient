using System;
using System.Collections.Generic;
using System.Net.Http;

namespace NameProject.RestClient.Configurations;

public class BaseRestClientSetting
{
    public HttpClient Client { get; set; }
    public Dictionary<string, string> DefaultRequestHeaders { get; set; } = [];
    public Uri BaseAddress { get; set; }
}
