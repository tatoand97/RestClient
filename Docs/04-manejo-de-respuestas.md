# Manejo de respuestas

`RestClientService` ofrece sobrecargas tipadas y sin procesar para consumir respuestas HTTP. Esta guía explica cómo interpretar resultados, validar estados y surfear errores.

## Deserialización automática
- Los métodos genéricos (`Get<T>`, `Post<T>`, `Put<T>`, `Delete<T>`) usan `System.Text.Json` con `PropertyNameCaseInsensitive = true`.
- Asegura que tus DTO usen nombres compatibles o agrega atributos `[JsonPropertyName]` cuando difieran.

```csharp
public sealed class OrderDto
{
    public Guid Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

var order = await restClient.Get<OrderDto>("Pedidos", "/v1/orders/123", ct);
```

Si la deserialización falla, se lanza `RestRequestFailedException` e incluye detalles en el log con el payload truncado.

## Acceso a HttpResponseMessage
Cuando necesitas encabezados personalizados, códigos de estado no exitosos o streams sin deserializar, emplea las sobrecargas que regresan `HttpResponseMessage`.

```csharp
using var response = await restClient.Get("Pedidos", "/v1/orders", ct);

if (response.StatusCode == HttpStatusCode.NoContent)
{
    return Array.Empty<OrderDto>();
}

var content = await response.Content.ReadAsStreamAsync(ct);
return await JsonSerializer.DeserializeAsync<OrderDto[]>(content, cancellationToken: ct) ?? Array.Empty<OrderDto>();
```

Recuerda desechar manualmente el mensaje (`using` o `await using`) para liberar sockets.

## Validación de estado
- `EnsureSuccessStatusAsync` valida que `IsSuccessStatusCode` sea verdadero y, en caso contrario, captura el cuerpo para diagnóstico.
- El mensaje del log incluye método, URL y estado numérico, lo que facilita correlacionar fallos.

## Excepciones relevantes
- `RestRequestFailedException`: encapsula el cuerpo y el `StatusCode` cuando la respuesta no es exitosa o la deserialización falla.
- `InvalidOperationException`: aparece durante la configuración si faltan valores requeridos.

Atrapa `RestRequestFailedException` para traducirla a respuestas específicas o reintentos en la aplicación.

```csharp
try
{
    return await restClient.Get<OrderDto>("Pedidos", "/v1/orders/456", ct);
}
catch (RestRequestFailedException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return null;
}
```

## Manejo de contenido grande
- La librería registra un máximo de 2048 caracteres del cuerpo en logs para evitar fuga de información sensible.
- Para contenido voluminoso considera usar streams (`ReadAsStreamAsync`) y procesarlo incrementalmente.

## JsonSerializerOptions personalizados
Si requieres opciones diferentes (por ejemplo `CamelCase` o conversores), crea un servicio adaptador que invoque `Get` o `Post` sin genéricos y deserialice manualmente con tus propias opciones.
