# Autenticacion y headers

La libreria soporta configuraciones flexibles de encabezados predeterminados y flujos OAuth2 mediante `TokenSetting`. Esta guia resume los escenarios habituales.

## Encabezados predeterminados
- Define `RestClient:Services[*]:DefaultRequestHeaders` como un objeto clave-valor.
- Los encabezados declarados reemplazan cualquier valor existente en `HttpClient.DefaultRequestHeaders`.
- Puedes inyectar valores dinamicos en tiempo de ejecucion implementando un `DelegatingHandler` personalizado que agregue encabezados por solicitud.

```json
{
  "DefaultRequestHeaders": {
    "Accept": "application/json",
    "X-Correlation-Id": "{TraceId}"
  }
}
```

## Tipos de autenticacion
La propiedad `Auth.Type` acepta los valores del enum `AuthenticationType`:

| Valor | Uso |
| ----- | --- |
| `None` | No se solicita token. |
| `OAuth2Header` | Credenciales expuestas como headers en `DefaultRequestHeaders`. |
| `OAuth2Body` | Credenciales en el cuerpo de la solicitud de token. |

### Configuracion OAuth2 con headers personalizados
Usa `OAuth2Header` cuando el proveedor de identidad requiere credenciales en headers. Define los pares clave-valor necesarios dentro de `Auth.DefaultRequestHeaders`, o deja que la normalizacion agregue las llaves basicas.

```json
{
  "Auth": {
    "Type": "OAuth2Header",
    "TokenUrl": "https://login.contoso.com/oauth/token",
    "ClientId": "catalog-app",
    "ClientSecret": "REEMPLAZA-ESTO",
    "Scope": "catalog.read",
    "Audience": "https://api.contoso.com",
    "DefaultRequestHeaders": {
      "X-Tenant": "contoso"
    }
  }
}
```

El normalizador incorpora automaticamente las llaves `ClientId`, `ClientSecret`, `GrantType`, `Scope` y `Audience` en `DefaultRequestHeaders` si no las declaraste. Puedes sobrescribir cualquiera agregando la misma clave con otro valor.

### Configuracion OAuth2 con credenciales en el cuerpo
Establece `Type` en `OAuth2Body` para enviar `client_id` y `client_secret` como parte del formulario.

```json
{
  "Auth": {
    "Type": "OAuth2Body",
    "TokenUrl": "https://login.contoso.com/oauth/token",
    "ClientId": "catalog-app",
    "ClientSecret": "REEMPLAZA-ESTO",
    "Scope": "catalog.write",
    "SendRequestBody": true,
    "ContentType": "application/x-www-form-urlencoded"
  }
}
```

Cuando `SendRequestBody` es `false`, la solicitud de token no lleva cuerpo; el proveedor debe aceptar credenciales mediante headers u otros mecanismos.

## Personalizacion de headers para la solicitud de token
- Declara `Auth.DefaultRequestHeaders` para agregar valores adicionales (por ejemplo `X-Tenant` o `Traceparent`). Si usas una clave ya generada (`ClientId`, `Scope`, etc.) se respetara tu valor.
- El handler reemplaza los headers existentes en `HttpClient.DefaultRequestHeaders` por los valores configurados.
- Si necesitas encabezados dinamicos, crea un `DelegatingHandler` adicional y registralo en la cadena de handlers del cliente.

## Renovacion y cache de tokens
- `OAuthTokenHandler` administra la adquisicion y cache interna de tokens por servicio.
- Cada cliente nombrado mantiene su propio `HttpClient` y handler, por lo que scopes diferentes requieren configuraciones separadas.
- Para invalidar la cache manualmente puedes reiniciar la aplicacion o reciclar el scope de DI cuando hospedes la libreria en procesos de corta vida.
