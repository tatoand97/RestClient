using System.Diagnostics;
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
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace NameProject.RestClient.Tests;

public sealed class RestClientBehaviorTests
{
    [Fact]
    public async Task TypedClientRegistration_InjectsCompanyRestClientIntoImplementationConstructor()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{"id":"123","status":"created"}"""));
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            new Dictionary<string, string?>
            {
                ["HttpClients:OrdersApi:BaseAddress"] = "https://orders.example/"
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
    public async Task OAuthAuthorizationHandler_PostBodyIsPreservedOnFirstSuccessfulRequest()
    {
        using var inner = new RecordingHandler(_ => JsonResponse("""{"ok":true}"""));
        using var invoker = CreateOAuthInvoker(inner, out _);
        using var request = CreateJsonPostRequest("""{"name":"first"}""");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(inner.Requests);
        Assert.Equal("""{"name":"first"}""", await inner.Requests[0].Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task OAuthAuthorizationHandler_PostBodyAndContentHeadersArePreservedAcrossUnauthorizedRetry()
    {
        var apiRequests = 0;
        using var inner = new RecordingHandler(_ =>
        {
            apiRequests++;
            return apiRequests == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : JsonResponse("""{"ok":true}""");
        });

        using var invoker = CreateOAuthInvoker(inner, out var tokenProvider);
        using var request = CreateJsonPostRequest("""{"name":"retry"}""");
        request.Content!.Headers.ContentLanguage.Add("en-US");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal("""{"name":"retry"}""", await inner.Requests[0].Content!.ReadAsStringAsync());
        Assert.Equal("""{"name":"retry"}""", await inner.Requests[1].Content!.ReadAsStringAsync());
        Assert.Equal("application/json", inner.Requests[0].Content!.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", inner.Requests[1].Content!.Headers.ContentType?.MediaType);
        Assert.Contains("en-US", inner.Requests[0].Content!.Headers.ContentLanguage);
        Assert.Contains("en-US", inner.Requests[1].Content!.Headers.ContentLanguage);
        Assert.Equal(2, tokenProvider.GetTokenCalls);
        Assert.Equal(1, tokenProvider.InvalidateCalls);
    }

    [Fact]
    public async Task OAuthAuthorizationHandler_RetriesExactlyOnceAfterUnauthorized()
    {
        using var inner = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var invoker = CreateOAuthInvoker(inner, out var tokenProvider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://orders.example/orders/1");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            new Dictionary<string, string?>
            {
                ["HttpClients:OrdersApi:BaseAddress"] = "https://orders.example/",
                ["HttpClients:OrdersApi:DefaultRequestHeaders:X-Client"] = "orders"
            });

        var client = provider.GetRequiredService<ITestOrdersApiClient>();

        await client.GetOrderAsync("123");

        Assert.True(handler.Requests[0].Headers.TryGetValues("X-Client", out var values));
        Assert.Equal("orders", Assert.Single(values));
    }

    [Fact]
    public async Task GetAsync_WithNoContentThrowsClearInvalidOperationException()
    {
        var client = CreateCompanyRestClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync<TestOrderDto>("/orders/123"));

        AssertTypedEmptyResponseMessage(exception);
    }

    [Fact]
    public async Task PostAsync_WithNoContentThrowsClearInvalidOperationException()
    {
        var client = CreateCompanyRestClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PostAsync<TestOrderDto>("/orders", new { Id = "123" }));

        AssertTypedEmptyResponseMessage(exception);
    }

    [Fact]
    public async Task PutAsync_WithEmptyOkThrowsClearInvalidOperationException()
    {
        var client = CreateCompanyRestClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty)
        }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PutAsync<TestOrderDto>("/orders/123", new { Id = "123" }));

        AssertTypedEmptyResponseMessage(exception);
    }

    [Fact]
    public async Task DeleteAsync_SucceedsWithNoContent()
    {
        var client = CreateCompanyRestClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        await client.DeleteAsync("/orders/123");
    }

    [Fact]
    public void ResilienceOptions_DefaultPresetIsTimeoutOnly()
    {
        var options = new ResilienceOptions();

        Assert.Equal(ResiliencePreset.TimeoutOnly, options.Preset);
        Assert.False(options.Retry.HandleTimeouts);
        Assert.False(options.Retry.RetryUnsafeMethods);
    }

    [Fact]
    public async Task TimeoutOnly_DoesNotRetry()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            BaseConfiguration("OrdersApi"));

        var exception = await Assert.ThrowsAsync<ExternalApiException>(
            () => provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123"));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task None_AddsNoCustomResilienceHandlerBeyondHttpClientTimeout()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            BaseConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:Resilience:Preset", "None"));

        await Assert.ThrowsAsync<ExternalApiException>(
            () => provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryTransient_RetriesInternalServerErrorForGet()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            RetryConfiguration("OrdersApi"));

        var order = await provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123");

        Assert.Equal("ok", order.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryTransient_RetriesHttpRequestExceptionForSafeMethods()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("network");
            }

            return JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            RetryConfiguration("OrdersApi"));

        var order = await provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123");

        Assert.Equal("ok", order.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryTransient_DoesNotRetryTimeoutRejectedExceptionByDefault()
    {
        using var handler = new RecordingHandler(_ => throw new TimeoutRejectedException("timeout"));
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            RetryConfiguration("OrdersApi"));

        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryHandleTimeouts_ExplicitlyEnablesTimeoutRetry()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutRejectedException("timeout");
            }

            return JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            RetryConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:Resilience:Retry:HandleTimeouts", "true"));

        var order = await provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123");

        Assert.Equal("ok", order.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryTransient_DoesNotRetryPostByDefault()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            RetryConfiguration("OrdersApi"));

        await Assert.ThrowsAsync<ExternalApiException>(
            () => provider.GetRequiredService<ITestOrdersApiClient>().CreateOrderAsync("123"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryUnsafeMethods_EnablesPostRetry()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            RetryConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:Resilience:Retry:RetryUnsafeMethods", "true"));

        var order = await provider.GetRequiredService<ITestOrdersApiClient>().CreateOrderAsync("123");

        Assert.Equal("ok", order.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryTransient_RespectsRetryAfterForTooManyRequests()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(150));
                return response;
            }

            return JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            RetryConfiguration("OrdersApi"));

        var stopwatch = Stopwatch.StartNew();
        await provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123");
        stopwatch.Stop();

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(100), $"Elapsed was {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task BackoffRetryTransient_UsesBoundedBackoffPolicy()
    {
        var attempts = 0;
        using var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            BaseConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:Resilience:Preset", "BackoffRetryTransient")
                .With("HttpClients:OrdersApi:Resilience:Retry:Attempts", "2")
                .With("HttpClients:OrdersApi:Resilience:Retry:BaseDelaySeconds", "0.02")
                .With("HttpClients:OrdersApi:Resilience:Retry:MaxDelaySeconds", "0.05")
                .With("HttpClients:OrdersApi:Resilience:Retry:UseJitter", "true"));

        var stopwatch = Stopwatch.StartNew();
        var order = await provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123");
        stopwatch.Stop();

        Assert.Equal("ok", order.Status);
        Assert.Equal(3, handler.Requests.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Elapsed was {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task TimeoutCircuitBreaker_DoesNotRetry()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            CircuitBreakerConfiguration("OrdersApi"));

        await Assert.ThrowsAsync<ExternalApiException>(
            () => provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("123"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterConfiguredFailureThreshold()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            CircuitBreakerConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:Resilience:CircuitBreaker:MinimumThroughput", "2"));

        var client = provider.GetRequiredService<ITestOrdersApiClient>();
        await Assert.ThrowsAsync<ExternalApiException>(() => client.GetOrderAsync("1"));
        await Assert.ThrowsAsync<ExternalApiException>(() => client.GetOrderAsync("2"));
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetOrderAsync("3"));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RateLimitedTimeout_LimitsConcurrentOutboundCalls()
    {
        using var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(100, cancellationToken);
            return JsonResponse("""{"id":"123","status":"ok"}""");
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            BaseConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:Resilience:Preset", "RateLimitedTimeout")
                .With("HttpClients:OrdersApi:Resilience:Timeout:Seconds", "1")
                .With("HttpClients:OrdersApi:Resilience:RateLimiter:PermitLimit", "1")
                .With("HttpClients:OrdersApi:Resilience:RateLimiter:QueueLimit", "1"));

        var client = provider.GetRequiredService<ITestOrdersApiClient>();
        await Task.WhenAll(client.GetOrderAsync("1"), client.GetOrderAsync("2"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, handler.MaxConcurrentRequests);
    }

    [Fact]
    public async Task RateLimitedTimeoutCircuitBreaker_CombinesLimiterTimeoutAndBreaker()
    {
        using var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(100, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var provider = BuildClientServices(
            "OrdersApi",
            handler,
            BaseConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:Resilience:Preset", "RateLimitedTimeoutCircuitBreaker")
                .With("HttpClients:OrdersApi:Resilience:Timeout:Seconds", "1")
                .With("HttpClients:OrdersApi:Resilience:RateLimiter:PermitLimit", "1")
                .With("HttpClients:OrdersApi:Resilience:RateLimiter:QueueLimit", "1")
                .With("HttpClients:OrdersApi:Resilience:CircuitBreaker:MinimumThroughput", "2"));

        var client = provider.GetRequiredService<ITestOrdersApiClient>();
        await Task.WhenAll(
            Assert.ThrowsAsync<ExternalApiException>(() => client.GetOrderAsync("1")),
            Assert.ThrowsAsync<ExternalApiException>(() => client.GetOrderAsync("2")));
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetOrderAsync("3"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, handler.MaxConcurrentRequests);
    }

    [Fact]
    public void IrrelevantNonDefaultStrategyOptions_FailValidation()
    {
        var configuration = BaseConfiguration("OrdersApi")
            .With("HttpClients:OrdersApi:Resilience:Preset", "TimeoutOnly")
            .With("HttpClients:OrdersApi:Resilience:Retry:Attempts", "1");

        var exception = Assert.Throws<InvalidOperationException>(() => BuildClientServices("OrdersApi", new RecordingHandler(_ => JsonResponse("{}")), configuration));

        Assert.Contains("does not support Resilience:Retry", exception.Message);
    }

    [Fact]
    public void StrictPresetValidation_FailsWhenIrrelevantSectionsArePresent()
    {
        var configuration = BaseConfiguration("OrdersApi")
            .With("HttpClients:OrdersApi:Resilience:Preset", "TimeoutOnly")
            .With("HttpClients:OrdersApi:Resilience:StrictPresetValidation", "true")
            .With("HttpClients:OrdersApi:Resilience:Retry:Attempts", "2");

        var exception = Assert.Throws<InvalidOperationException>(() => BuildClientServices("OrdersApi", new RecordingHandler(_ => JsonResponse("{}")), configuration));

        Assert.Contains("does not support Resilience:Retry", exception.Message);
    }

    [Fact]
    public async Task MultipleTypedClients_UseIndependentBaseUrlsAuthHeadersAndResiliencePipelines()
    {
        using var tokenHandler = new RecordingHandler(request =>
        {
            var scope = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains("payments.read", StringComparison.Ordinal)
                ? "payments-token"
                : "orders-token";
            return JsonResponse(TokenJson(scope, expiresIn: 3600));
        });

        var orderAttempts = 0;
        using var ordersHandler = new RecordingHandler(_ =>
        {
            orderAttempts++;
            return orderAttempts == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse("""{"id":"o-1","status":"ok"}""");
        });
        using var paymentsHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var provider = BuildServices(
            BaseConfiguration("OrdersApi")
                .With("HttpClients:OrdersApi:DefaultRequestHeaders:X-Api", "orders")
                .With("HttpClients:OrdersApi:Resilience:Preset", "RetryTransient")
                .With("HttpClients:OrdersApi:Resilience:Retry:Attempts", "1")
                .With("HttpClients:OrdersApi:Auth:Type", "OAuth2Body")
                .With("HttpClients:OrdersApi:Auth:TokenUrl", "https://identity.example/token")
                .With("HttpClients:OrdersApi:Auth:ClientId", "orders-client")
                .With("HttpClients:OrdersApi:Auth:ClientSecret", "secret")
                .With("HttpClients:OrdersApi:Auth:Scope", "orders.read")
                .With("HttpClients:PaymentsApi:BaseAddress", "https://payments.example/")
                .With("HttpClients:PaymentsApi:DefaultRequestHeaders:X-Api", "payments")
                .With("HttpClients:PaymentsApi:Resilience:Preset", "TimeoutOnly")
                .With("HttpClients:PaymentsApi:Resilience:Timeout:Seconds", "1")
                .With("HttpClients:PaymentsApi:Auth:Type", "OAuth2Body")
                .With("HttpClients:PaymentsApi:Auth:TokenUrl", "https://identity.example/token")
                .With("HttpClients:PaymentsApi:Auth:ClientId", "payments-client")
                .With("HttpClients:PaymentsApi:Auth:ClientSecret", "secret")
                .With("HttpClients:PaymentsApi:Auth:Scope", "payments.read"),
            services =>
            {
                services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => tokenHandler);
                services.AddCompanyRestClient<ITestOrdersApiClient, TestOrdersApiClient>(BuildConfigurationSection("OrdersApi"));
                services.AddCompanyRestClient<ITestPaymentsApiClient, TestPaymentsApiClient>(BuildConfigurationSection("PaymentsApi"));
                services.AddHttpClient("OrdersApi").ConfigurePrimaryHttpMessageHandler(() => ordersHandler);
                services.AddHttpClient("PaymentsApi").ConfigurePrimaryHttpMessageHandler(() => paymentsHandler);
            });

        var order = await provider.GetRequiredService<ITestOrdersApiClient>().GetOrderAsync("o-1");
        await Assert.ThrowsAsync<ExternalApiException>(() => provider.GetRequiredService<ITestPaymentsApiClient>().GetPaymentAsync("p-1"));

        Assert.Equal("ok", order.Status);
        Assert.Equal(2, ordersHandler.Requests.Count);
        Assert.Single(paymentsHandler.Requests);
        Assert.Equal(new Uri("https://orders.example/orders/o-1"), ordersHandler.Requests[0].RequestUri);
        Assert.Equal(new Uri("https://payments.example/payments/p-1"), paymentsHandler.Requests[0].RequestUri);
        Assert.Equal("orders", ordersHandler.Requests[0].Headers.GetValues("X-Api").Single());
        Assert.Equal("payments", paymentsHandler.Requests[0].Headers.GetValues("X-Api").Single());
        Assert.Equal("Bearer orders-token", ordersHandler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal("Bearer payments-token", paymentsHandler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal(2, tokenHandler.Requests.Count);
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

        var client = CreateCompanyRestClient(handler);

        var exception = await Assert.ThrowsAsync<ExternalApiException>(() => client.GetAsync<TestOrderDto>("/orders/123"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("Bad Request", exception.ReasonPhrase);
        Assert.Equal(HttpMethod.Get, exception.Method);
        Assert.Equal("invalid payload", exception.ResponseBody);
        Assert.Equal(new Uri("https://orders.example/orders/123"), exception.RequestUri);
    }

    private static HttpMessageInvoker CreateOAuthInvoker(RecordingHandler inner, out FakeAccessTokenProvider tokenProvider)
    {
        tokenProvider = new FakeAccessTokenProvider();
        var handler = new OAuthAuthorizationHandler("OrdersApi", CreateAuthOptions(), tokenProvider)
        {
            InnerHandler = inner
        };

        return new HttpMessageInvoker(handler);
    }

    private static HttpRequestMessage CreateJsonPostRequest(string body)
        => new(HttpMethod.Post, "https://orders.example/orders")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static CompanyRestClient CreateCompanyRestClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://orders.example/") };
        return new CompanyRestClient(
            httpClient,
            new DefaultRestClientSerializer(),
            new DefaultHttpErrorHandler(NullLogger<DefaultHttpErrorHandler>.Instance),
            NullLogger<CompanyRestClient>.Instance);
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
            ClientSecret = "secret",
            Scope = scope
        };

    private static void AssertTypedEmptyResponseMessage(InvalidOperationException exception)
    {
        Assert.Contains("succeeded", exception.Message);
        Assert.Contains("response body was empty", exception.Message);
        Assert.Contains("Typed methods require a JSON response body", exception.Message);
        Assert.Contains("No-content endpoints should use SendAsync, raw methods, DeleteAsync, or a dedicated no-content method", exception.Message);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string TokenJson(string token, int expiresIn)
        => $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn}}}""";

    private static Dictionary<string, string?> BaseConfiguration(string clientName)
        => new()
        {
            [$"HttpClients:{clientName}:BaseAddress"] = clientName == "PaymentsApi" ? "https://payments.example/" : "https://orders.example/",
            [$"HttpClients:{clientName}:TimeoutSeconds"] = "100"
        };

    private static Dictionary<string, string?> RetryConfiguration(string clientName)
        => BaseConfiguration(clientName)
            .With($"HttpClients:{clientName}:Resilience:Preset", "RetryTransient")
            .With($"HttpClients:{clientName}:Resilience:Retry:Attempts", "1")
            .With($"HttpClients:{clientName}:Resilience:Retry:BaseDelaySeconds", "0.01")
            .With($"HttpClients:{clientName}:Resilience:Retry:MaxDelaySeconds", "0.05");

    private static Dictionary<string, string?> CircuitBreakerConfiguration(string clientName)
        => BaseConfiguration(clientName)
            .With($"HttpClients:{clientName}:Resilience:Preset", "TimeoutCircuitBreaker")
            .With($"HttpClients:{clientName}:Resilience:Timeout:Seconds", "1")
            .With($"HttpClients:{clientName}:Resilience:CircuitBreaker:FailureThresholdPercentage", "50")
            .With($"HttpClients:{clientName}:Resilience:CircuitBreaker:MinimumThroughput", "2")
            .With($"HttpClients:{clientName}:Resilience:CircuitBreaker:SamplingDurationSeconds", "5")
            .With($"HttpClients:{clientName}:Resilience:CircuitBreaker:BreakDurationSeconds", "5");

    private static ServiceProvider BuildClientServices(
        string clientName,
        HttpMessageHandler handler,
        Dictionary<string, string?> configurationValues)
        => BuildServices(
            configurationValues,
            services =>
            {
                services.AddCompanyRestClient<ITestOrdersApiClient, TestOrdersApiClient>(
                    BuildConfigurationSection(clientName));
                services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            });

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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        private int _activeRequests;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = [];
        public int MaxConcurrentRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CloneForAssertionsAsync(request, cancellationToken).ConfigureAwait(false));
            var active = Interlocked.Increment(ref _activeRequests);
            MaxConcurrentRequests = Math.Max(MaxConcurrentRequests, active);
            try
            {
                var response = await _respond(request, cancellationToken).ConfigureAwait(false);
                response.RequestMessage ??= request;
                return response;
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        private static async Task<HttpRequestMessage> CloneForAssertionsAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy
            };

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var content = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                clone.Content = new ByteArrayContent(content);
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
        Task<TestOrderDto> CreateOrderAsync(string orderId);
    }

    private sealed class TestOrdersApiClient(ICompanyRestClient client) : ITestOrdersApiClient
    {
        public Task<TestOrderDto> GetOrderAsync(string orderId)
            => client.GetAsync<TestOrderDto>($"/orders/{orderId}");

        public Task<TestOrderDto> CreateOrderAsync(string orderId)
            => client.PostAsync<TestOrderDto>("/orders", new { Id = orderId });
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

internal static class DictionaryExtensions
{
    public static Dictionary<string, string?> With(this Dictionary<string, string?> values, string key, string? value)
    {
        values[key] = value;
        return values;
    }
}
