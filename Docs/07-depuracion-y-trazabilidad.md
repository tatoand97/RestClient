# Depuración y trazabilidad

La librería emite logs y excepciones que facilitan diagnosticar problemas en integraciones HTTP. Esta guía describe cómo aprovecharlos.

## Logging estructurado
- `RestClientService` usa `ILogger<RestClientService>` para registrar reintentos, errores de estado y problemas de deserialización.
- Configura el nivel de log `Information` o `Debug` para la categoría `NameProject.RestClient` en `appsettings.json`.

```json
{
  "Logging": {
    "LogLevel": {
      "NameProject.RestClient": "Information"
    }
  }
}
```

## Identificadores de correlación
- Agrega encabezados como `X-Correlation-Id` en `DefaultRequestHeaders` para trazar solicitudes end-to-end.
- Inyecta valores dinámicos (por ejemplo `Activity.Current.TraceId`) mediante un handler personalizado que se ejecute antes de cada solicitud.

## Diagnóstico de errores
- Cuando una respuesta falla, la excepción `RestRequestFailedException` contiene el `StatusCode` y mensaje de detalle.
- El log incluye método HTTP, URL completa, código y motivo (`ReasonPhrase`), además del cuerpo truncado (hasta 2048 caracteres).
- Captura estos eventos en tu sistema de monitoreo (Application Insights, Seq, ELK, etc.) para crear alertas.

## Trazas distribuidas
- Habilita `ActivitySource` en tu aplicación y propaga los encabezados `traceparent` y `tracestate`. El cliente respeta los encabezados existentes y los envía tal cual.
- Usa herramientas como OpenTelemetry para correlacionar los spans del RestClient con otros componentes.

## Herramientas de red
- Para inspeccionar tráfico local, combina el cliente con utilidades como Fiddler o mitmproxy. Configura el sistema operativo para enrutar las solicitudes del proceso por el proxy.
- En contenedores o pipelines, usa `tcpdump` o capturas de Application Gateway para validar TLS, cabeceras y tiempos de respuesta.
