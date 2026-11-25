using SyncAgent.Aspel;

namespace SyncAgent.Publishing;

/// <summary>Resultado de un envío confirmado por la API.</summary>
/// <param name="Unchanged">La huella coincidió con la última corrida aplicada: no se enviaron productos.</param>
/// <param name="Summary">Resumen de la API; nulo si <paramref name="Unchanged"/>.</param>
public sealed record PublishResult(Guid RunId, bool Unchanged, string InventoryHash, SyncRunSummary? Summary);

/// <summary>Punto donde el agente entrega los artículos leídos. La implementación real es <see cref="HttpCatalogPublisher"/>.</summary>
public interface ICatalogPublisher
{
    /// <summary>Envía el inventario. Solo devuelve si la API lo confirmó; si no, lanza <see cref="ApiPublishException"/>.</summary>
    Task<PublishResult> PublishAsync(IReadOnlyList<AspelProduct> products, CancellationToken cancellationToken);
}

/// <summary>Comprobación de <c>--test</c>: la API responde y la llave es válida. No envía productos.</summary>
public interface IApiConnectivityCheck
{
    /// <summary>Lanza <see cref="ApiPublishException"/> si la API no responde o rechaza la llave.</summary>
    Task PingAsync(CancellationToken cancellationToken);
}
