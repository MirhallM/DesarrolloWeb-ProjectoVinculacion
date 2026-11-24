using backend.Sync;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

/// <summary>
/// Recepción del catálogo que envía el agente de sincronización (US-06). Todas las rutas exigen la llave del agente.
/// </summary>
[ApiController]
[Route("api/sync")]
[RequireSyncAgentKey]
public sealed class SyncController(TimeProvider clock) : ControllerBase
{
    /// <summary>Comprueba que la API responde y que la llave es válida. No cambia nada.</summary>
    [HttpGet("ping")]
    [ProducesResponseType<SyncPingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Ping() => Ok(new SyncPingResponse(true, clock.GetUtcNow().UtcDateTime));
}
