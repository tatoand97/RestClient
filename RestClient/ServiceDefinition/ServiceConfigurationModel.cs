using NameProject.RestClient.Configurations;

namespace NameProject.RestClient.ServiceDefinition;

internal sealed record ServiceConfigurationModel(int HttpClientRetry, double HttpClientDelay, Dictionary<string, RestClientServiceSetting> Services);
