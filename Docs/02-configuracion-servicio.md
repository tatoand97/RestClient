# Configuracion del servicio

Esta guia detalla como registrar la libreria y declarar la configuracion requerida en archivos de entorno (por ejemplo `appsettings.json`).

## Registro en el contenedor
Invoca el configurador desde la inicializacion de tu aplicacion. El metodo de extension `AddHttpClientConfiguration` encapsula la llamada.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHttpClientConfiguration(builder.Configuration)
    .AddScoped<IMyService, MyService>();
```

Si prefieres llamar al configurador directamente, usa:

```csharp
RestClientServiceConfigurator.Configure(builder.Services, builder.Configuration);
```

## Estructura de configuracion
Agrega una seccion `RestClient` con los valores de reintento y la lista de servicios disponibles.

```json
{
  "RestClient": {
    "HttpClientRetry": 3,
    "HttpClientDelay": 2.5,
    "Services": [
      {
        "Name": "Catalogo",
        "BaseUrl": "https://api.contoso.com/",
        "DefaultRequestHeaders": {
          "Accept": "application/json",
          "X-Tenant": "contoso"
        },
        "Auth": {
          "Type": "OAuth2Header",
          "TokenUrl": "https://login.contoso.com/oauth/token",
          "ClientId": "catalog-app",
          "ClientSecret": "REEMPLAZA-ESTO",
          "Scope": "catalog.read catalog.write",
          "Audience": "https://api.contoso.com",
          "GrantType": "client_credentials",
          "SendRequestBody": true,
          "DefaultRequestHeaders": {
            "X-Environment": "production"
          }
        }
      }
    ]
  }
}
```

## Reglas claves
- `HttpClientRetry` y `HttpClientDelay` deben ser mayores a cero; de lo contrario se lanzara una excepcion de configuracion.
- Cada servicio requiere `Name` unico y un `BaseUrl` absoluto.
- Los encabezados declarados se aplican tal cual al `HttpClient` denominado.
- Si la seccion `Auth` esta presente se crea y adjunta un `OAuthHandler` para administrar tokens.
- Al menos un servicio debe estar definido dentro de `Services`.

## Configuracion por entorno
- Ajusta credenciales y endpoints por ambiente usando `appsettings.Development.json`, `appsettings.Staging.json`, etc.
- Cuando utilices Azure App Configuration o Key Vault, vincula los valores sensibles (`ClientSecret`) mediante referencias para evitar exponerlos en archivos.
