# NameProject.RestClient

Typed HTTP clients for external APIs with per-client configuration, OAuth token automation, retries, JSON serialization, and consistent HTTP error handling.

## Registration

```csharp
builder.Services.AddCompanyRestClient<IOrdersApiClient, OrdersApiClient>(
    builder.Configuration.GetSection("HttpClients:OrdersApi"));
```

Each typed client implementation receives the per-client `ICompanyRestClient` in its constructor:

```csharp
public sealed class OrdersApiClient : IOrdersApiClient
{
    private readonly ICompanyRestClient _client;

    public OrdersApiClient(ICompanyRestClient client)
    {
        _client = client;
    }

    public Task<OrderDto> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        => _client.GetAsync<OrderDto>($"/orders/{orderId}", cancellationToken);
}
```

Application code depends on the typed API client:

```csharp
public sealed class OrderService(IOrdersApiClient ordersApi)
{
    public Task<OrderDto> GetAsync(string orderId, CancellationToken cancellationToken)
        => ordersApi.GetOrderAsync(orderId, cancellationToken);
}
```

## Configuration

```json
{
  "HttpClients": {
    "OrdersApi": {
      "BaseAddress": "https://apim.company.com/orders/",
      "TimeoutSeconds": 30,
      "DefaultRequestHeaders": {
        "Ocp-Apim-Subscription-Key": "value"
      },
      "Retry": {
        "Attempts": 3,
        "BaseDelaySeconds": 2
      },
      "Auth": {
        "Type": "OAuth2Body",
        "TokenUrl": "https://identity.company.com/oauth2/token",
        "GrantType": "client_credentials",
        "Scope": "orders.read",
        "Audience": "",
        "ClientId": "client-id",
        "ClientSecret": "client-secret",
        "ContentType": "application/x-www-form-urlencoded",
        "SendRequestBody": true
      }
    }
  }
}
```

## Validation

```powershell
dotnet restore RestClient.slnx
dotnet build RestClient.slnx --configuration Release
dotnet test RestClient.slnx --configuration Release
```
