namespace backend.Sync;

/// <summary>Respuesta de <c>GET /api/sync/ping</c>: la API responde y la llave es válida.</summary>
public sealed record SyncPingResponse(bool Ok, DateTime ServerTimeUtc);

/// <summary>
/// <c>POST /api/sync/runs</c>. El agente genera <see cref="RunId"/> para que reintentar el inicio sea idempotente.
/// </summary>
public sealed record StartSyncRunRequest(Guid RunId, int ItemCount, string? InventoryHash, string? AgentVersion);

/// <summary>Respuesta al iniciar: <c>running</c> (enviar los lotes) o <c>unchanged</c> (ya registrada, no enviar nada).</summary>
public sealed record StartSyncRunResponse(Guid RunId, string Status, int? MaxBatchSize, DateTime? FinishedAt);

/// <summary>Un artículo tal como viene de Aspel. <see cref="Code"/> se guarda exacto, sin recortar.</summary>
public sealed record SyncBatchItem(string? Code, string? Description, double? Stock, string? Status, double? Price);

/// <summary><c>POST /api/sync/runs/{runId}/batches</c>. Reenviar la misma secuencia reemplaza ese lote.</summary>
public sealed record SyncBatchRequest(int Sequence, List<SyncBatchItem>? Items);

/// <summary><c>POST /api/sync/runs/{runId}/complete</c>: lo que el agente envió, para comprobar que llegó todo.</summary>
public sealed record CompleteSyncRunRequest(int ItemCount, int BatchCount);

/// <summary>Resumen de una corrida aplicada.</summary>
public sealed record SyncRunSummary(
    Guid RunId,
    string Status,
    int Read,
    int Inserted,
    int Updated,
    int Deactivated,
    int Rejected,
    IReadOnlyList<string> RejectedCodes,
    DateTime? FinishedAt);

/// <summary><c>POST /api/sync/runs/{runId}/abort</c>: el agente abandona la corrida por un error propio.</summary>
public sealed record AbortSyncRunRequest(string? Reason);

/// <summary>Valores de <c>status</c> en las respuestas.</summary>
public static class SyncResponseStatuses
{
    public const string Running = "running";
    public const string Unchanged = "unchanged";
    public const string Succeeded = "succeeded";
}

/// <summary>Valores de <c>type</c> de los ProblemDetails de /api/sync (el agente los usa para explicar el fallo).</summary>
public static class SyncProblemTypes
{
    public const string InvalidRequest = "sync-invalid-request";
    public const string RunNotFound = "sync-run-not-found";
    public const string RunInProgress = "sync-run-in-progress";
    public const string RunNotActive = "sync-run-not-active";
    public const string DuplicateCode = "sync-duplicate-code";
    public const string CountMismatch = "sync-count-mismatch";
    public const string SafetyBrake = "sync-safety-brake";
}
