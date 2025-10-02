namespace NameProject.RestClient.Configurations;

public abstract class BaseRestClientSetting
{
    public Dictionary<string, string> DefaultRequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public required Uri BaseAddress { get; set; }
}
