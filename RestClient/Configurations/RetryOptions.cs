namespace NameProject.RestClient.Configurations;

public sealed class RetryOptions
{
    public int Attempts { get; set; } = 3;
    public double BaseDelaySeconds { get; set; } = 1;
}
