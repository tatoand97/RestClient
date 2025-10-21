# Personalizacion avanzada

Una vez configurado el cliente base, puedes ajustar la resiliencia, los handlers y el comportamiento de `HttpClient` para cubrir escenarios avanzados.

## Ajuste de politicas de reintento
- `HttpClientRetry` controla la cantidad de reintentos ante errores transitorios.
- `HttpClientDelay` define la base (en segundos) para el backoff con jitter.
- Considera valores mas bajos para servicios idempotentes sensibles y mas altos para operaciones con baja concurrencia.

## Configurar timeouts y tamanos de buffer
Usa `IOptions<HttpClientFactoryOptions>` para modificar el cliente nombrado despues de la configuracion inicial.

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
- Registra `DelegatingHandler` personalizados para auditoria, trazas o firma de peticiones.
- Aprovecha que los clientes se crean por nombre y encadena handlers segun la necesidad de cada servicio.

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

## Personalizacion del flujo de token
- Extiende `OAuthTokenHandler` heredando y registrando tu propia implementacion si necesitas almacenar tokens en cache externa (por ejemplo Redis).
- Alternativamente, crea un handler decorador que lea el token emitido y lo replique en telemetria.

## Separacion de scopes
- Declara multiples servicios con el mismo `BaseUrl` pero scopes distintos para aislar permisos.
- Centraliza las constantes de nombre y scope en una clase estatica para mantener la configuracion sincronizada con el codigo.

## Ajustes por entorno
- Carga configuraciones adicionales mediante `IConfiguration` (por ejemplo `appsettings.Production.json`) y evita hardcodear valores.
- Para pruebas locales usa `dotnet user-secrets` y mantente alineado con la estructura `RestClient`.
