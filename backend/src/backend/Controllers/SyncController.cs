using backend.Sync;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

/// <summary>
/// Recepción del catálogo que envía el agente de sincronización (US-06). Todas las rutas exigen la llave del agente.
/// Protocolo: iniciar corrida → lotes → completar (ver docs/architecture.md, sección 5).
/// No usa cookies de sesión; se excluye del filtro CSRF global porque autentica con <c>X-Sync-Agent-Key</c>.
/// </summary>
[ApiController]
[Route("api/sync")]
[RequireSyncAgentKey]
[IgnoreAntiforgeryToken]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class SyncController(SyncService sync, TimeProvider clock) : ControllerBase
{
    /// <summary>Lotes de unos 106 KB; el reenvío de Next.js corta a partir de 10 MB.</summary>
    public const long MaxBatchBodyBytes = 4 * 1024 * 1024;

    /// <summary>Comprueba que la API responde y que la llave es válida. No cambia nada.</summary>
    [HttpGet("ping")]
    [ProducesResponseType<SyncPingResponse>(StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new SyncPingResponse(true, clock.GetUtcNow().UtcDateTime));

    /// <summary>Inicia una corrida, o la registra como "sin cambios" si la huella coincide con la última aplicada.</summary>
    [HttpPost("runs")]
    [ProducesResponseType<StartSyncRunResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<StartSyncRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartRun(StartSyncRunRequest request, CancellationToken ct) =>
        ToResponse(await sync.StartAsync(request, ct));

    /// <summary>Guarda un lote en el área de preparación. Reenviar la misma secuencia la reemplaza.</summary>
    [HttpPost("runs/{runId:guid}/batches")]
    [RequestSizeLimit(MaxBatchBodyBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StageBatch(Guid runId, SyncBatchRequest request, CancellationToken ct) =>
        ToResponse(await sync.StageBatchAsync(runId, request, ct));

    /// <summary>Aplica la corrida en una transacción. Idempotente: un reintento devuelve el mismo resumen.</summary>
    /// <remarks>No recibe el CancellationToken de la solicitud a propósito: ver <see cref="SyncService.CompleteAsync"/>.</remarks>
    [HttpPost("runs/{runId:guid}/complete")]
    [ProducesResponseType<SyncRunSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CompleteRun(Guid runId, CompleteSyncRunRequest request) =>
        ToResponse(await sync.CompleteAsync(runId, request));

    /// <summary>El agente abandona la corrida: queda fallida y se borra su preparación.</summary>
    [HttpPost("runs/{runId:guid}/abort")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AbortRun(Guid runId, AbortSyncRunRequest request, CancellationToken ct) =>
        ToResponse(await sync.AbortAsync(runId, request, ct));

    private IActionResult ToResponse(SyncResult result) => result.Kind switch
    {
        SyncResultKind.Ok => Ok(result.Body),
        SyncResultKind.Created => StatusCode(StatusCodes.Status201Created, result.Body),
        SyncResultKind.NoContent => NoContent(),
        _ => Problem(
            statusCode: result.Kind switch
            {
                SyncResultKind.BadRequest => StatusCodes.Status400BadRequest,
                SyncResultKind.NotFound => StatusCodes.Status404NotFound,
                SyncResultKind.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status422UnprocessableEntity,
            },
            type: result.ProblemType,
            title: result.Title,
            detail: result.Detail),
    };
}
