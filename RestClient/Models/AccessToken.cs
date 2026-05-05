namespace NameProject.RestClient.Models;

public sealed record AccessToken(string TokenType, string Value, DateTimeOffset ExpiresAt);
