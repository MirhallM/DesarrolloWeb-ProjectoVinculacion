using Microsoft.Extensions.Logging;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Publishing;
using SyncAgent.State;

namespace SyncAgent;

/// <summary>
/// Una ejecución completa del agente (<c>--once</c>): leer Aspel, entregar los artículos al publicador y dejar
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

            await publisher.PublishAsync(result.Products, cancellationToken);

            var now = clock.GetUtcNow();
            state.LastResult = SyncResults.Succeeded;
            state.LastError = null;
            state.LastSuccessAt = now;
            state.LastRunFinishedAt = now;
            state.ConsecutiveFailures = 0;
            state.ProductsRead = summary.TotalRows;
            state.QueryDurationMs = (long)result.Metrics.QueryDuration.TotalMilliseconds;
            state.PeakWorkingSetMb = Math.Round(ProcessMemory.ToMegabytes(ProcessMemory.Capture().PeakWorkingSetBytes), 1);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallo no previsto (p. ej. del publicador): se deja constancia en el estado y se propaga.
            RecordFailure(state, $"{ex.GetType().Name}: {ex.Message}");
            throw;
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
