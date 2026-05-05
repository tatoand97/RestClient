using NameProject.RestClient.Interfaces;

namespace NameProject.RestClient.Examples;

public sealed class OrdersApiClient(ICompanyRestClient client) : IOrdersApiClient
{
    public Task<OrderDto> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        => client.GetAsync<OrderDto>($"/orders/{orderId}", cancellationToken);
}
