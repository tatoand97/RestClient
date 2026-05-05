namespace NameProject.RestClient.Interfaces;

public interface IHttpErrorHandler
{
    Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken);
}
