# NameProject.RestClient

Typed HTTP clients for external APIs with per-client configuration, OAuth token automation, JSON serialization, controlled resilience presets, and consistent HTTP error handling.

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
      "Resilience": {
        "Preset": "TimeoutCircuitBreaker",
        "Timeout": {
          "Seconds": 10
        },
        "CircuitBreaker": {
          "FailureThresholdPercentage": 50,
          "MinimumThroughput": 20,
          "SamplingDurationSeconds": 30,
          "BreakDurationSeconds": 30
        }
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

## Resilience presets

Retry is not enabled by default. The default preset is `TimeoutOnly`, which bounds latency without amplifying load against a downstream service that may already be scaling or degraded.

Timeout failures are not retried by default. `Retry.HandleTimeouts` defaults to `false` because timeout is a latency boundary; retrying it can turn one slow call into multiple slow calls. `POST`, `PUT`, `PATCH`, `DELETE`, and `CONNECT` are also not retried unless `Retry.RetryUnsafeMethods` is explicitly enabled. Callers that enable unsafe retries should use idempotency keys or domain-level duplicate protection.

Available presets:

- `None`: no custom resilience handler; only `HttpClient.Timeout`.
- `TimeoutOnly`: timeout only. This is the default.
- `RetryTransient`: retry transient HTTP responses and `HttpRequestException` for safe methods.
- `BackoffRetryTransient`: retry transient failures with exponential backoff, jitter, and max delay.
- `TimeoutRetryTransient`: timeout plus retry; timeout retry requires `Retry.HandleTimeouts = true`.
- `TimeoutCircuitBreaker`: timeout plus circuit breaker, no retry.
- `RetryCircuitBreaker`: retry plus circuit breaker.
- `TimeoutBackoffCircuitBreaker`: timeout plus backoff retry plus circuit breaker.
- `RateLimitedTimeout`: concurrency limiter plus timeout, no retry.
- `RateLimitedTimeoutCircuitBreaker`: concurrency limiter plus timeout plus circuit breaker, no retry.

Recommended presets:

- Latency-sensitive internal API: `TimeoutOnly` or `TimeoutCircuitBreaker`.
- Service under scaling pressure: `TimeoutCircuitBreaker` or `RateLimitedTimeoutCircuitBreaker`.
- APIM or MuleSoft integration: `RateLimitedTimeout` or `RateLimitedTimeoutCircuitBreaker`.
- Idempotent GET/read API with transient failures: `RetryTransient` or `BackoffRetryTransient`.
- Critical idempotent integration: `TimeoutBackoffCircuitBreaker`.
- Non-idempotent writes: `TimeoutOnly` or `TimeoutCircuitBreaker`; avoid retry unless duplicate protection exists.

Example without retry:

```json
{
  "HttpClients": {
    "LowLatencyApi": {
      "BaseAddress": "https://internal.company.com/",
      "TimeoutSeconds": 5,
      "Resilience": {
        "Preset": "TimeoutOnly",
        "Timeout": {
          "Seconds": 3
        }
      }
    }
  }
}
```

Example with outbound concurrency protection:

```json
{
  "HttpClients": {
    "MuleCustomerApi": {
      "BaseAddress": "https://mule.company.com/customers/",
      "TimeoutSeconds": 20,
      "Resilience": {
        "Preset": "RateLimitedTimeoutCircuitBreaker",
        "Timeout": {
          "Seconds": 8
        },
        "RateLimiter": {
          "PermitLimit": 50,
          "QueueLimit": 0
        },
        "CircuitBreaker": {
          "FailureThresholdPercentage": 50,
          "MinimumThroughput": 30,
          "SamplingDurationSeconds": 30,
          "BreakDurationSeconds": 20
        }
      }
    }
  }
}
```

Example for idempotent reads with backoff:

```json
{
  "HttpClients": {
    "CatalogApi": {
      "BaseAddress": "https://apim.company.com/catalog/",
      "TimeoutSeconds": 20,
      "Resilience": {
        "Preset": "BackoffRetryTransient",
        "Retry": {
          "Attempts": 2,
          "BaseDelaySeconds": 1,
          "MaxDelaySeconds": 5,
          "UseJitter": true,
          "HandleTimeouts": false,
          "RetryUnsafeMethods": false
        }
      }
    }
  }
}
```

## Typed response bodies

`GetAsync<T>`, `PostAsync<T>`, and `PutAsync<T>` require a JSON response body. If the request succeeds with `204 No Content` or an empty body, these methods throw a clear `InvalidOperationException` instead of returning `default(T)`.

No-content endpoints should use `SendAsync`, raw methods such as `GetAsync`, `PostAsync`, `PutAsync`, `DeleteRawAsync`, `DeleteAsync`, or a dedicated no-content method on the typed client.

## Validation

```powershell
dotnet restore RestClient.slnx
dotnet build RestClient.slnx --configuration Release
dotnet test RestClient.slnx --configuration Release
```
