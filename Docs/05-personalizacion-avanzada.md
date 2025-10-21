# Personalización avanzada

Una vez configurado el cliente base, puedes ajustar la resiliencia, los handlers y el comportamiento de `HttpClient` para cubrir escenarios avanzados.

## Ajuste de políticas de reintento
- `HttpClientRetry` controla la cantidad de reintentos ante errores transitorios.
- `HttpClientDelay` define la base (en segundos) para el backoff con jitter.
- Considera valores más bajos para servicios idempotentes sensibles y más altos para operaciones con baja concurrencia.

## Configurar timeouts y tamaños de buffer
Usa `IOptions<HttpClientFactoryOptions>` para modificar el cliente nombrado después de la configuración inicial.

```csharp
services.Configure<HttpClientFactoryOptions>("Catalogo", options =>
{
    options.HttpClientActions.Add(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestVersion = HttpVersion.Version20;
    });
});
```

## Inyectar handlers adicionales
- Registra `DelegatingHandler` personalizados para auditoría, trazas o firma de peticiones.
- Aprovecha que los clientes se crean por nombre y encadena handlers según la necesidad de cada servicio.

```csharp
services.AddTransient<TelemetryHandler>();

services.Configure<HttpClientFactoryOptions>("Catalogo", options =>
{
    options.HttpMessageHandlerBuilderActions.Add(builder =>
    {
        builder.AdditionalHandlers.Add(builder.Services.GetRequiredService<TelemetryHandler>());
    });
});
```

## Personalización del flujo de token
- Extiende `OAuthTokenHandler` heredando y registrando tu propia implementación si necesitas almacenar tokens en caché externa (por ejemplo Redis).
- Alternativamente, crea un handler decorador que lea el token emitido y lo replique en telemetría.

## Separación de scopes
- Declara múltiples servicios con el mismo `BaseUrl` pero scopes distintos para aislar permisos.
- Centraliza las constantes de nombre y scope en una clase estática para mantener la configuración sincronizada con el código.

## Ajustes por entorno
- Carga configuraciones adicionales mediante `IConfiguration` (por ejemplo `appsettings.Production.json`) y evita hardcodear valores.
- Para pruebas locales usa `dotnet user-secrets` y mantente alineado con la estructura `RestClient`.
