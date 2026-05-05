using System.Text.Json;
using NameProject.RestClient.Interfaces;

namespace NameProject.RestClient.Services;

public sealed class DefaultRestClientSerializer : IRestClientSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);

    public T Deserialize<T>(string content)
        => JsonSerializer.Deserialize<T>(content, JsonOptions)
           ?? throw new JsonException($"Response body deserialized to null for type {typeof(T).FullName}.");
}
