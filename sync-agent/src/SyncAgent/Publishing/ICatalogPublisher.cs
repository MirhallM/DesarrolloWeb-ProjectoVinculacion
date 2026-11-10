using Microsoft.Extensions.Logging;
using SyncAgent.Aspel;

namespace SyncAgent.Publishing;

/// <summary>
/// Punto donde el agente entrega los artículos leídos. En US-06 se agrega aquí la implementación que los envía
/// a la API por HTTPS (en lotes, con la credencial propia del agente). El resto del agente no cambia.
/// </summary>
public interface ICatalogPublisher
{
    Task PublishAsync(IReadOnlyList<AspelProduct> products, CancellationToken cancellationToken);
}

/// <summary>Implementación provisional (US-05): no envía nada, solo deja constancia en el log.</summary>
public sealed class NoOpCatalogPublisher(ILogger<NoOpCatalogPublisher> logger) : ICatalogPublisher
{
    public Task PublishAsync(IReadOnlyList<AspelProduct> products, CancellationToken cancellationToken)
    {
        logger.LogInformation("Envío a la API pendiente (US-06): {Count} artículos leídos, no se envía nada.", products.Count);
        return Task.CompletedTask;
    }
}
