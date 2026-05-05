using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NameProject.RestClient;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Exceptions;
using NameProject.RestClient.Handlers;
using NameProject.RestClient.Interfaces;
using NameProject.RestClient.Models;
using NameProject.RestClient.Services;
using Xunit;

namespace NameProject.RestClient.Tests;

public sealed class RestClientBehaviorTests
{
    [Fact]
    public async Task TypedClientRegistration_InjectsCompanyRestClientIntoImplementationConstructor()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{"id":"123","status":"created"}"""));
        using var provider = BuildServices(
            new Dictionary<string, string?>
            {
                ["HttpClients:OrdersApi:BaseAddress"] = "https://orders.example/",
                ["HttpClients:OrdersApi:Retry:Attempts"] = "0"
            },
            services =>
            {
                services.AddCompanyRestClient<ITestOrdersApiClient, TestOrdersApiClient>(
                    BuildConfigurationSection("OrdersApi"));
                services.AddHttpClient("OrdersApi").ConfigurePrimaryHttpMessageHandler(() => handler);
            });

        var client = provider.GetRequiredService<ITestOrdersApiClient>();

        var response = await client.GetOrderAsync("123");

        Assert.Equal("123", response.Id);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AccessTokenProvider_RequestsTokenOnlyOnceWhileValid()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(TokenJson("token-1", expiresIn: 3600)));
        var provider = CreateTokenProvider(handler);
        var options = CreateAuthOptions(scope: "orders.read");

        var first = await provider.GetTokenAsync("OrdersApi", options, CancellationToken.None);
        var second = await provider.GetTokenAsync("OrdersApi", options, CancellationToken.None);

        Assert.Equal("token-1", first.Value);
        Assert.Equal(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AccessTokenProvider_RefreshesTokenWhenExpired()
    {
        var tokenRequests = 0;
        using var handler = new RecordingHandler(_ =>
        {
            tokenRequests++;
            var value = tokenRequests == 1 ? "token-1" : "token-2";
            return JsonResponse(TokenJson(value, expiresIn: 1));
        });

        var provider = CreateTokenProvider(handler);
        var options = CreateAuthOptions(scope: "orders.read");

        var first = await provider.GetTokenAsync("OrdersApi", options, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        var second = await provider.GetTokenAsync("OrdersApi", options, CancellationToken.None);

        Assert.Equal("token-1", first.Value);
        Assert.Equal("token-2", second.Value);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OAuthAuthorizationHandler_InvalidatesAndRetriesOnceAfterUnauthorized()
    {
        var apiRequests = 0;
        using var inner = new RecordingHandler(_ =>
        {
            apiRequests++;
            return apiRequests == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : JsonResponse("""{"ok":true}""");
        });

        var tokenProvider = new FakeAccessTokenProvider();
        using var handler = new OAuthAuthorizationHandler("OrdersApi", CreateAuthOptions(), tokenProvider)
        {
            InnerHandler = inner
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://orders.example/orders/1"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(2, tokenProvider.GetTokenCalls);
        Assert.Equal(1, tokenProvider.InvalidateCalls);
        Assert.Equal("Bearer token-1", inner.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal("Bearer token-2", inner.Requests[1].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task TypedClient_AppliesDefaultHeaders()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{"id":"123","status":"ok"}"""));
        using var provider = BuildServices(
            new Dictionary<string, string?>
            {
                ["HttpClients:OrdersApi:BaseAddress"] = "https://orders.example/",
                ["HttpClients:OrdersApi:DefaultRequestHeaders:X-Client"] = "orders",
                ["HttpClients:OrdersApi:Retry:Attempts"] = "0"
            },
            services =>
            {
                services.AddCompanyRestClient<ITestOrdersApiClient, TestOrdersApiClient>(
                    BuildConfigurationSection("OrdersApi"));
                services.AddHttpClient("OrdersApi").ConfigurePrimaryHttpMessageHandler(() => handler);
            });

        var client = provider.GetRequiredService<ITestOrdersApiClient>();

        await client.GetOrderAsync("123");

        Assert.True(handler.Requests[0].Headers.TryGetValues("X-Client", out var values));
        Assert.Equal("orders", Assert.Single(values));
    }

    [Fact]
    public async Task TypedClient_AppliesRetryPolicyPerClient()
    {
        var apiRequests = 0;
        using var handler = new RecordingHandler(_ =>
        {
            apiRequests++;
            return apiRequests == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildServices(
            new Dictionary<string, string?>
            {
                ["HttpClients:OrdersApi:BaseAddress"] = "https://orders.example/",
                ["HttpClients:OrdersApi:Retry:Attempts"] = "1",
                ["HttpClients:OrdersApi:Retry:BaseDelaySeconds"] = "0.01"
            },
            services =>
            {
                services.AddCompanyRestClient<ITestOrdersApiClient, TestOrdersApiClient>(
                    BuildConfigurationSection("OrdersApi"));
                services.AddHttpClient("OrdersApi").ConfigurePrimaryHttpMessageHandler(() => handler);
            });

        var client = provider.GetRequiredService<ITestOrdersApiClient>();

        var order = await client.GetOrderAsync("123");

        Assert.Equal("ok", order.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Serializer_DeserializesCaseInsensitiveJson()
    {
        var serializer = new DefaultRestClientSerializer();

        var order = serializer.Deserialize<TestOrderDto>("""{"ID":"123","STATUS":"ok"}""");

        Assert.Equal("123", order.Id);
        Assert.Equal("ok", order.Status);
    }

    [Fact]
    public async Task CompanyRestClient_ThrowsExternalApiExceptionForNonSuccessResponse()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent("invalid payload")
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://orders.example/") };
        var client = new CompanyRestClient(
            httpClient,
            new DefaultRestClientSerializer(),
            new DefaultHttpErrorHandler(NullLogger<DefaultHttpErrorHandler>.Instance),
            NullLogger<CompanyRestClient>.Instance);

        var exception = await Assert.ThrowsAsync<ExternalApiException>(() => client.GetAsync<TestOrderDto>("/orders/123"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("Bad Request", exception.ReasonPhrase);
        Assert.Equal(HttpMethod.Get, exception.Method);
        Assert.Equal("invalid payload", exception.ResponseBody);
        Assert.Equal(new Uri("https://orders.example/orders/123"), exception.RequestUri);
    }

    [Fact]
    public async Task MultipleTypedClients_UseIndependentBaseUrlsAuthHeadersAndRetrySettings()
    {
        using var tokenHandler = new RecordingHandler(request =>
        {
            var scope = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains("payments.read", StringComparison.Ordinal)
                ? "payments-token"
                : "orders-token";
            return JsonResponse(TokenJson(scope, expiresIn: 3600));
        });

        using var ordersHandler = new RecordingHandler(_ => JsonResponse("""{"id":"o-1","status":"ok"}"""));
        using var paymentsHandler = new RecordingHandler(_ => JsonResponse("""{"id":"p-1","status":"ok"}"""));

        using var provider = BuildServices(
            new Dictionary<string, string?>
            {
                ["HttpClients:OrdersApi:BaseAddress"] = "https://orders.example/",
                ["HttpClients:OrdersApi:DefaultRequestHeaders:X-Api"] = "orders",
                ["HttpClients:OrdersApi:Retry:Attempts"] = "0",
                ["HttpClients:OrdersApi:Auth:Type"] = "OAuth2Body",
                ["HttpClients:OrdersApi:Auth:TokenUrl"] = "https://identity.example/token",
                ["HttpClients:OrdersApi:Auth:ClientId"] = "orders-client",
                ["HttpClients:OrdersApi:Auth:ClientSecret"] = "secret",
                ["HttpClients:OrdersApi:Auth:Scope"] = "orders.read",

                ["HttpClients:PaymentsApi:BaseAddress"] = "https://payments.example/",
                ["HttpClients:PaymentsApi:DefaultRequestHeaders:X-Api"] = "payments",
                ["HttpClients:PaymentsApi:Retry:Attempts"] = "2",
                ["HttpClients:PaymentsApi:Retry:BaseDelaySeconds"] = "0.01",
                ["HttpClients:PaymentsApi:Auth:Type"] = "OAuth2Body",
                ["HttpClients:PaymentsApi:Auth:TokenUrl"] = "https://identity.example/token",
                ["HttpClients:PaymentsApi:Auth:ClientId"] = "payments-client",
                ["HttpClients:PaymentsApi:Auth:ClientSecret"] = "secret",
                ["HttpClients:PaymentsApi:Auth:Scope"] = "payments.read"
            },
            services =>
            {
                services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => tokenHandler);
                services.AddCompanyRestClient<ITestOrdersApiClient, TestOrdersApiClient>(BuildConfigurationSection("OrdersApi"));
                services.AddCompanyRestClient<ITestPaymentsApiClient, TestPaymentsApiClient>(BuildConfigurationSection("PaymentsApi"));
                services.AddHttpClient("OrdersApi").ConfigurePrimaryHttpMessageHandler(() => ordersHandler);
                services.AddHttpClient("PaymentsApi").ConfigurePrimaryHttpMessageHandler(() => paymentsHandler);
            });

        await provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("o-1");
        await provider.GetRequiredService<ITestPaymentsApiClient>().GetPaymentAsync("p-1");

        Assert.Equal(new Uri("https://orders.example/orders/o-1"), ordersHandler.Requests[0].RequestUri);
        Assert.Equal(new Uri("https://payments.example/payments/p-1"), paymentsHandler.Requests[0].RequestUri);
        Assert.Equal("orders", ordersHandler.Requests[0].Headers.GetValues("X-Api").Single());
        Assert.Equal("payments", paymentsHandler.Requests[0].Headers.GetValues("X-Api").Single());
        Assert.Equal("Bearer orders-token", ordersHandler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal("Bearer payments-token", paymentsHandler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal(2, tokenHandler.Requests.Count);
    }

    private static DefaultAccessTokenProvider CreateTokenProvider(HttpMessageHandler handler)
    {
        var factory = new StaticHttpClientFactory(handler);
        return new DefaultAccessTokenProvider(
            factory,
            new DefaultRestClientSerializer(),
            NullLogger<DefaultAccessTokenProvider>.Instance);
    }

    private static AuthOptions CreateAuthOptions(string scope = "orders.read")
        => new()
        {
            Type = AuthenticationType.OAuth2Body,
            TokenUrl = new Uri("https://identity.example/token"),
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Scope = scope
        };

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string TokenJson(string token, int expiresIn)
        => $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn}}}""";

    private static ServiceProvider BuildServices(
        Dictionary<string, string?> configurationValues,
        Action<IServiceCollection> configureServices)
    {
        TestConfiguration.Current = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        configureServices(services);
        return services.BuildServiceProvider();
    }

    private static IConfigurationSection BuildConfigurationSection(string clientName)
        => TestConfiguration.Current.GetSection($"HttpClients:{clientName}");

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneForAssertions(request));
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }

        private static HttpRequestMessage CloneForAssertions(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(content);
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeAccessTokenProvider : IAccessTokenProvider
    {
        public int GetTokenCalls { get; private set; }
        public int InvalidateCalls { get; private set; }

        public Task<AccessToken> GetTokenAsync(string clientName, AuthOptions options, CancellationToken cancellationToken)
        {
            GetTokenCalls++;
            return Task.FromResult(new AccessToken("Bearer", $"token-{GetTokenCalls}", DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        public Task InvalidateAsync(string clientName, CancellationToken cancellationToken)
        {
            InvalidateCalls++;
            return Task.CompletedTask;
        }
    }

    private static class TestConfiguration
    {
        public static IConfiguration Current { get; set; } = new ConfigurationBuilder().Build();
    }

    private sealed record TestOrderDto(string Id, string? Status);

    private interface ITestOrdersApiClient
    {
        Task<TestOrderDto> GetOrderAsync(string orderId);
    }

    private sealed class TestOrdersApiClient(ICompanyRestClient client) : ITestOrdersApiClient
    {
        public Task<TestOrderDto> GetOrderAsync(string orderId)
            => client.GetAsync<TestOrderDto>($"/orders/{orderId}");
    }

    private interface ITestPaymentsApiClient
    {
        Task<TestOrderDto> GetPaymentAsync(string paymentId);
    }

    private sealed class TestPaymentsApiClient(ICompanyRestClient client) : ITestPaymentsApiClient
    {
        public Task<TestOrderDto> GetPaymentAsync(string paymentId)
            => client.GetAsync<TestOrderDto>($"/payments/{paymentId}");
    }
}
