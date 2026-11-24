using Microsoft.Extensions.Logging;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Publishing;
using SyncAgent.State;

namespace SyncAgent;

/// <summary>
/// Una ejecución completa del agente (<c>--once</c>): leer Aspel, enviar los artículos a la API y dejar
/// el resultado guardado en disco para la siguiente ejecución.
/// </summary>
public sealed class SyncCycle(
    AspelProductLoader loader,
    ICatalogPublisher publisher,
    SyncStateStore stateStore,
    SecretRedactor redactor,
    TimeProvider clock,
    ILogger<SyncCycle> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        state.LastRunStartedAt = clock.GetUtcNow();
        logger.LogInformation("Sincronización iniciada.");

        try
        {
            var result = await loader.LoadAsync(cancellationToken);
            var summary = CatalogSummaryBuilder.From(result.Products);
            logger.LogInformation(
                "Lectura correcta: {Total} artículos ({Active} activos, {Inactive} de baja, {NoPrice} activos sin precio) " +
                "en {QueryMs:N0} ms, {Attempts} intento(s).",
                summary.TotalRows, summary.Active, summary.Inactive, summary.ActiveWithoutPrice,
                result.Metrics.QueryDuration.TotalMilliseconds, result.Attempts);

            var published = await publisher.PublishAsync(result.Products, cancellationToken);
            LogPublished(published);

            // Solo aquí, con la confirmación de la API, cuenta como sincronización exitosa.
            var now = clock.GetUtcNow();
            state.LastResult = SyncResults.Succeeded;
            state.LastError = null;
            state.LastSuccessAt = now;
            state.LastRunFinishedAt = now;
            state.ConsecutiveFailures = 0;
            state.ProductsRead = summary.TotalRows;
            state.QueryDurationMs = (long)result.Metrics.QueryDuration.TotalMilliseconds;
            state.PeakWorkingSetMb = Math.Round(ProcessMemory.ToMegabytes(ProcessMemory.Capture().PeakWorkingSetBytes), 1);
            state.LastRunId = published.RunId;
            state.LastInventoryHash = published.InventoryHash;
            state.LastPublish = published.Unchanged
                ? new PublishSummary { Mode = "unchanged" }
                : new PublishSummary
                {
                    Mode = "full",
                    Inserted = published.Summary!.Inserted,
                    Updated = published.Summary.Updated,
                    Deactivated = published.Summary.Deactivated,
                    Rejected = published.Summary.Rejected,
                };

            if (!TrySave(state))
            {
                return ExitCodes.UnexpectedError;
            }

            logger.LogInformation("Sincronización terminada correctamente.");
            return ExitCodes.Success;
        }
        catch (AspelReadException ex)
        {
            // El mensaje ya viene sin usuario ni contraseña.
            logger.LogError("Sincronización fallida: {Error}", ex.Message);
            RecordFailure(state, ex.Message);
            return ExitCodes.SqlFailure;
        }
        catch (ApiPublishException ex)
        {
            // El mensaje ya viene sin la llave. El catálogo en la API no cambió.
            logger.LogError("No se pudo enviar el catálogo a la API: {Error}", ex.Message);
            RecordFailure(state, ex.Message);
            return ExitCodes.ApiFailure;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallo no previsto: se deja constancia en el estado y se propaga.
            RecordFailure(state, $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private void LogPublished(PublishResult published)
    {
        if (published.Unchanged)
        {
            logger.LogInformation("API: sin cambios desde la última sincronización (corrida {RunId}).", published.RunId);
            return;
        }

        var s = published.Summary!;
        logger.LogInformation(
            "API: corrida {RunId} aplicada. {Read} leídos, {Inserted} insertados, {Updated} actualizados, " +
            "{Deactivated} desactivados, {Rejected} rechazados.",
            published.RunId, s.Read, s.Inserted, s.Updated, s.Deactivated, s.Rejected);

        if (s.Rejected > 0 && s.RejectedCodes is { Count: > 0 } codes)
        {
            logger.LogWarning("Artículos rechazados por la API (corríjalos en Aspel): {Codes}",
                string.Join(", ", codes.Select(c => $"'{c}'")));
        }
    }

    private void RecordFailure(SyncState state, string message)
    {
        state.LastResult = SyncResults.Failed;
        state.LastError = redactor.Redact(message); // última defensa antes de escribir en disco
        state.LastRunFinishedAt = clock.GetUtcNow();
        state.ConsecutiveFailures++;
        // LastSuccessAt NO se toca: indica cuándo fue la última vez que sí se sincronizó.
        TrySave(state);
    }

    private bool TrySave(SyncState state)
    {
        try
        {
            stateStore.Save(state);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("No se pudo guardar el estado en disco ({Reason}). Revise los permisos de la carpeta data.", ex.GetType().Name);
            return false;
        }
    }
}
