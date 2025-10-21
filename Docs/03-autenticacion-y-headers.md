# Autenticación y headers

La librería soporta configuraciones flexibles de encabezados predeterminados y flujos OAuth2 mediante `TokenSetting`. Esta guía cubre los escenarios habituales.

## Encabezados predeterminados
- Define `RestClient:Services[*]:DefaultRequestHeaders` como un objeto clave-valor.
- Los encabezados declarados reemplazan cualquier valor existente en `HttpClient.DefaultRequestHeaders`.
- Puedes inyectar valores dinámicos en tiempo de ejecución implementando `DelegatingHandler` personalizado que agregue encabezados por solicitud.

```json
{
  "DefaultRequestHeaders": {
    "Accept": "application/json",
    "X-Correlation-Id": "{TraceId}"
  }
}
```

## Tipos de autenticación
La propiedad `Auth.Type` acepta los valores del enum `AuthenticationType`:

| Valor | Uso |
| ----- | --- |
| `None` | No se solicita token. |
| `OAuth2Header` | Credenciales en encabezado `Authorization: Basic`. |
| `OAuth2Body` | Credenciales en el cuerpo del request de token. |

### Configuración OAuth2 con encabezado Basic
Usa `OAuth2Header` cuando el proveedor de identidad requiere autenticación básica.

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

El handler genera el header `Authorization` automáticamente, por lo que cualquier valor existente con esa clave se elimina para evitar duplicidad.

### Configuración OAuth2 con credenciales en el cuerpo
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

Cuando `SendRequestBody` es `false`, la solicitud de token no lleva cuerpo; el proveedor debe aceptar credenciales mediante encabezados u otros mecanismos.

## Personalización de headers para la solicitud de token
- Declara `Auth.DefaultRequestHeaders` para agregar valores (por ejemplo `X-Tenant`).
- El handler limpia cualquier header duplicado antes de agregar los nuevos.
- Puedes usar valores como `User-Agent` o `Traceparent` si el proveedor de tokens lo soporta.

## Renovación y caché de tokens
- `OAuthTokenHandler` administra la adquisición y caché interna de tokens por servicio.
- Cada cliente nombrado mantiene su propio `HttpClient` y handler, por lo que scopes diferentes requieren configuraciones separadas.
- Para invalidar caché manualmente puedes reiniciar la aplicación o publicar un evento interno que fuerce una nueva instancia del handler (por ejemplo, reciclando el scope DI en hospedajes de corta vida).
