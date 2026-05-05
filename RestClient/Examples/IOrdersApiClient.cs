namespace NameProject.RestClient.Examples;

public interface IOrdersApiClient
{
    Task<OrderDto> GetOrderAsync(string orderId, CancellationToken cancellationToken = default);
}
