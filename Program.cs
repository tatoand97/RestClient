using RestClient.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClientConfiguration(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();