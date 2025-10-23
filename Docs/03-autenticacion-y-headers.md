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
| `OAuth2Header` | Credenciales en headers configurados dentro de `DefaultRequestHeaders` (por ejemplo `Authorization: Basic`). |
| `OAuth2Body` | Credenciales en el cuerpo de la solicitud de token. |

### Configuracion OAuth2 con headers personalizados
Usa `OAuth2Header` cuando el proveedor de identidad requiere credenciales en headers. Define los pares clave-valor necesarios dentro de `Auth.DefaultRequestHeaders`, ya sea `Authorization: Basic` u otros encabezados propios del proveedor.

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
      "Authorization": "Basic Y2F0YWxvZzpjYXQtc2VjcmV0",
      "X-Tenant": "contoso"
    }
  }
}
```

El handler respeta exactamente los valores declarados en `DefaultRequestHeaders`; si necesitas `Authorization` u otro header debes declararlo de forma explicita.

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
- Declara `Auth.DefaultRequestHeaders` para agregar valores (por ejemplo `X-Tenant`, `Authorization` o `Traceparent`).
- El handler reemplaza los headers existentes en `HttpClient.DefaultRequestHeaders` por los valores configurados.
- Si necesitas encabezados dinamicos, crea un `DelegatingHandler` adicional y registralo en la cadena de handlers del cliente.

## Renovacion y cache de tokens
- `OAuthTokenHandler` administra la adquisicion y cache interna de tokens por servicio.
- Cada cliente nombrado mantiene su propio `HttpClient` y handler, por lo que scopes diferentes requieren configuraciones separadas.
- Para invalidar la cache manualmente puedes reiniciar la aplicacion o reciclar el scope de DI cuando hospedes la libreria en procesos de corta vida.
