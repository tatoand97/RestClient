using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NameProject.RestClient;
using NameProject.RestClient.Common;
using NameProject.RestClient.Configurations;
using NameProject.RestClient.Handlers;

namespace RestClient.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHttpClientConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var serviceConfiguration = GetServiceConfiguration(configuration);

        services.AddHttpClient();
        services.AddTransient<TuyaOAuthHandler>();

        services.AddHttpClient(Constants.TUYASERVICEKEYINGRESS, client =>
        {
            client.BaseAddress = serviceConfiguration.TuyaIngressService;
        });

        services.AddHttpClient(Constants.TUYASERVICEKEY, client =>
        {
            client.BaseAddress = serviceConfiguration.TuyaService;
            ConfigureHeaders(client, serviceConfiguration.TokenSetting.DefaultRequestHeaders);
        }).AddHttpMessageHandler<TuyaOAuthHandler>();

        services.AddHttpClientWrapper(options =>
        {
            options.HttpClientRetry = serviceConfiguration.HttpClientRetry;
            options.HttpClientDelay = serviceConfiguration.HttpClientDelay;
            options.Services = new Dictionary<string, RestClientServiceSetting>(StringComparer.OrdinalIgnoreCase)
            {
                [Constants.TUYASERVICEKEYINGRESS] = new RestClientServiceSetting
                {
                    BaseAddress = serviceConfiguration.TuyaIngressService,
                },
                [Constants.TUYASERVICEKEY] = new RestClientServiceSetting
                {
                    BaseAddress = serviceConfiguration.TuyaService,
                    DefaultRequestHeaders = new Dictionary<string, string>(serviceConfiguration.TokenSetting.DefaultRequestHeaders, StringComparer.OrdinalIgnoreCase),
                    TokenRequest = serviceConfiguration.TokenSetting
                }
            };
        });

        return services;
    }

    private static ServiceConfigurationModel GetServiceConfiguration(IConfiguration configuration)
    {
        var rawConfiguration = configuration
            .GetSection(ServiceConfiguration.SectionName)
            .Get<ServiceConfiguration>()
            ?? throw new InvalidOperationException($"Missing configuration section '{ServiceConfiguration.SectionName}'.");

        if (rawConfiguration.TokenSetting is null)
        {
            throw new InvalidOperationException($"Missing configuration section '{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TokenSetting)}'.");
        }

        var tuyaService = GetRequiredUri(rawConfiguration.TuyaService, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TuyaService)}");
        var tuyaIngressService = GetRequiredUri(rawConfiguration.TuyaIngressService, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TuyaIngressService)}");
        var tokenBaseAddress = GetRequiredUri(rawConfiguration.TokenSetting.BaseAddress, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TokenSetting)}:{nameof(TokenSettingConfiguration.BaseAddress)}");

        var tokenHeaders = rawConfiguration.TokenSetting.DefaultRequestHeaders is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(rawConfiguration.TokenSetting.DefaultRequestHeaders, StringComparer.OrdinalIgnoreCase);

        var tokenSetting = new TokenSetting
        {
            Path = GetRequiredString(rawConfiguration.TokenSetting.Path, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TokenSetting)}:{nameof(TokenSettingConfiguration.Path)}"),
            GrantType = GetRequiredString(rawConfiguration.TokenSetting.GrantType, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TokenSetting)}:{nameof(TokenSettingConfiguration.GrantType)}"),
            ClientId = GetRequiredString(rawConfiguration.TokenSetting.ClientId, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TokenSetting)}:{nameof(TokenSettingConfiguration.ClientId)}"),
            Scope = GetRequiredString(rawConfiguration.TokenSetting.Scope, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TokenSetting)}:{nameof(TokenSettingConfiguration.Scope)}"),
            ClientSecret = GetRequiredString(rawConfiguration.TokenSetting.ClientSecret, $"{ServiceConfiguration.SectionName}:{nameof(ServiceConfiguration.TokenSetting)}:{nameof(TokenSettingConfiguration.ClientSecret)}"),
            ContentType = string.IsNullOrWhiteSpace(rawConfiguration.TokenSetting.ContentType)
                ? "application/x-www-form-urlencoded"
                : rawConfiguration.TokenSetting.ContentType!,
            DefaultRequestHeaders = tokenHeaders,
            BaseAddress = tokenBaseAddress
        };

        return new ServiceConfigurationModel(
            rawConfiguration.HttpClientRetry,
            rawConfiguration.HttpClientDelay,
            tuyaIngressService,
            tuyaService,
            tokenSetting);
    }

    private static void ConfigureHeaders(HttpClient client, Dictionary<string, string> headers)
    {
        client.DefaultRequestHeaders.Clear();
        foreach (var header in headers)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }

    private static string GetRequiredString(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be provided.");
        }

        return value;
    }

    private static Uri GetRequiredUri(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be provided.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be a valid absolute URI.");
        }

        return uri;
    }

    private sealed record ServiceConfigurationModel(int HttpClientRetry, double HttpClientDelay, Uri TuyaIngressService, Uri TuyaService, TokenSetting TokenSetting);

    private sealed class ServiceConfiguration
    {
        public const string SectionName = "RestClient";

        public int HttpClientRetry { get; set; }
        public double HttpClientDelay { get; set; }
        public string? TuyaIngressService { get; set; }
        public string? TuyaService { get; set; }
        public TokenSettingConfiguration? TokenSetting { get; set; }
    }

    private sealed class TokenSettingConfiguration
    {
        public string? Path { get; set; }
        public string? GrantType { get; set; }
        public string? ClientId { get; set; }
        public string? Scope { get; set; }
        public string? ClientSecret { get; set; }
        public string? ContentType { get; set; }
        public Dictionary<string, string>? DefaultRequestHeaders { get; set; }
        public string? BaseAddress { get; set; }
    }
}
