# Manejo de respuestas

`RestClientService` ofrece sobrecargas tipadas y sin procesar para consumir respuestas HTTP. Esta guia explica como interpretar resultados, validar estados y surfear errores.

## Deserializacion automatica
- Los metodos genericos (`Get<T>`, `Post<T>`, `Put<T>`, `Delete<T>`) usan `System.Text.Json` con `PropertyNameCaseInsensitive = true`.
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

Si la deserializacion falla, se lanza `RestRequestFailedException` e incluye detalles en el log con el payload truncado.

## Acceso a HttpResponseMessage
Cuando necesitas encabezados personalizados, codigos de estado no exitosos o streams sin deserializar, emplea las sobrecargas que regresan `HttpResponseMessage`.

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

## Validacion de estado
- `EnsureSuccessStatusAsync` valida que `IsSuccessStatusCode` sea verdadero y, en caso contrario, captura el cuerpo para diagnostico.
- El mensaje del log incluye metodo, URL y estado numerico, lo que facilita correlacionar fallos.

## Excepciones relevantes
- `RestRequestFailedException`: encapsula el cuerpo y el `StatusCode` cuando la respuesta no es exitosa o la deserializacion falla.
- `InvalidOperationException`: aparece durante la configuracion si faltan valores requeridos.

Atrapa `RestRequestFailedException` para traducirla a respuestas especificas o reintentos aplicacion.

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
- La libreria registra un maximo de 2048 caracteres del cuerpo en logs para evitar fuga de informacion sensible.
- Para contenido voluminoso considera usar streams (`ReadAsStreamAsync`) y procesarlo incrementalmente.

## JsonSerializerOptions personalizados
Si requieres opciones diferentes (por ejemplo `CamelCase` o conversores), crea un servicio adaptador que invoque `Get` o `Post` sin genericos y deserialice manualmente con tus propias opciones.
