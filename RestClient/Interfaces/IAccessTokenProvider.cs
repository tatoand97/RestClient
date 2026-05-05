using NameProject.RestClient.Configurations;
using NameProject.RestClient.Models;

namespace NameProject.RestClient.Interfaces;

public interface IAccessTokenProvider
{
    Task<AccessToken> GetTokenAsync(string clientName, AuthOptions options, CancellationToken cancellationToken);
    Task InvalidateAsync(string clientName, CancellationToken cancellationToken);
}
