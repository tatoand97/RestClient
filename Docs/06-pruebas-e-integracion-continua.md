# Pruebas e integración continua

Esta guía recomienda estrategias para validar consumidores del cliente REST y automatizar la ejecución en pipelines.

## Pruebas unitarias
- Abstrae dependencias consumiendo `IRestClientService`; usa herramientas como Moq o NSubstitute para simular respuestas.
- Verifica flujos de error configurando mocks que arrojen `RestRequestFailedException`.

```csharp
var clientMock = new Mock<IRestClientService>();
clientMock
    .Setup(c => c.Get<OrderDto>("Pedidos", It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new OrderDto { Id = Guid.NewGuid(), Status = "Created" });

var sut = new OrderFacade(clientMock.Object);
```

## Pruebas de contrato
- Ejecuta `WebApplicationFactory` o `TestServer` y usa `IHttpClientFactory` para obtener el cliente nombrado y validar headers, timeouts y rutas.
- Integra herramientas como Pact o WireMock para emular servicios externos y asegurar que la configuración `RestClient` responde correctamente a cambios de contrato.

## Pruebas de integración
- Inyecta `IRestClientService` real apuntando a un entorno de prueba y supervisa los logs para detectar errores de autenticación o timeouts.
- Usa variables de entorno para cambiar `BaseUrl` y credenciales dentro del pipeline sin modificar archivos.

## Cobertura en pipelines
- Incluye un paso que valide la estructura de configuración (por ejemplo, deserializando `appsettings.json` y asegurando que `RestClient:Services` no esté vacío).
- Ejecuta pruebas con `dotnet test` y publica resultados y cobertura como parte del reporte del pipeline.

## Observabilidad de pruebas
- Habilita logging en nivel `Debug` para la categoría `NameProject.RestClient` cuando corras pruebas para capturar las solicitudes firmadas.
- Consolida los logs en artefactos del pipeline para diagnosticar fallos intermitentes.
