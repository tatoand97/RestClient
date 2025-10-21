# Casos de uso tipicos

Esta guia resume los escenarios mas comunes al consumir `IRestClientService`. Todos los ejemplos asumen que el servicio ya fue configurado mediante `RestClientServiceConfigurator.Configure`.

## Antes de iniciar
- Inyecta `IRestClientService` en las clases que actuaran como consumidores.
- Usa el nombre del servicio configurado (`RestClient:Services[*]:Name`) para seleccionar el cliente apropiado.
- Define rutas relativas (`path`) comenzando con `/` para que se combinen con `BaseUrl`.

## Ejemplo: solicitudes GET tipicas
Utiliza los metodos genericos cuando la respuesta se mapea a un DTO, o los metodos que regresan `HttpResponseMessage` cuando necesitas acceso completo al mensaje.

```csharp
public sealed record ProductDto(Guid Id, string Name, decimal Price);

public class CatalogController : ControllerBase
{
    private readonly IRestClientService restClient;

    public CatalogController(IRestClientService restClient)
    {
        this.restClient = restClient;
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(CancellationToken ct)
    {
        var products = await restClient.Get<IReadOnlyList<ProductDto>>("Catalogo", "/api/products", ct);
        return Ok(products);
    }
}
```

Cuando necesites inspeccionar encabezados o el cuerpo crudo, usa la sobrecarga que devuelve `HttpResponseMessage` y maneja la disposicion del mensaje.

## Ejemplo: solicitudes POST con payload JSON
Los metodos `Post` aceptan payload como `object` o `string`. La version de objeto serializa usando `System.Text.Json` con equivalencia de nombres insensible a mayusculas.

```csharp
public async Task<Guid> CreateProductAsync(ProductDto input, CancellationToken ct)
{
    var response = await restClient.Post<ProductDto>("Catalogo", "/api/products", input, ct);
    return response.Id;
}
```

Si el servicio devuelve un identificador simple en texto plano, cambia a la sobrecarga que regresa `HttpResponseMessage` y procesa manualmente el contenido.

## Ejemplo: PUT y DELETE
- `Put<T>` sigue la misma firma que `Post<T>` y es ideal para operaciones idempotentes.
- `Delete<T>` permite deserializar la respuesta de una eliminacion que regrese contenido (por ejemplo, el recurso final).

```csharp
public Task DeleteProductAsync(Guid id, CancellationToken ct)
{
    return restClient.Delete("Catalogo", $"/api/products/{id}", ct);
}
```

## Manejo de errores
Todas las operaciones realizan validaciones de estado y lanzan `RestRequestFailedException` cuando la respuesta no es exitosa o la deserializacion falla. Envuelve tus llamadas en bloques `try-catch` para capturar detalles y traducirlos a errores de dominio.

```csharp
try
{
    await restClient.Delete("Catalogo", "/api/products/999", ct);
}
catch (RestRequestFailedException ex)
{
    logger.LogWarning(ex, "El servicio remoto rechazo la solicitud");
    throw new ProductNotFoundException();
}
```

## Reintentos automatizados
La implementacion se apoya en Polly y utiliza `HttpClientRetry` y `HttpClientDelay` desde configuracion. Ajusta estos valores segun los SLA de los servicios remotos. Los reintentos aplican para errores transitorios (`5xx`, problemas de red, etc.).

## Buenas practicas
- Evita construir rutas con concatenaciones manuales; utiliza `Uri.EscapeDataString` para parametros dinamicos.
- Centraliza los nombres de servicio en constantes para prevenir errores tipograficos.
- Considera crear adaptadores que traduzcan excepciones a respuestas HTTP propias de tu API.
- Aprovecha `CancellationToken` para propagar cancelaciones desde la capa web o jobs.
