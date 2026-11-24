namespace backend.Sync;

/// <summary>Respuesta de <c>GET /api/sync/ping</c>: la API responde y la llave es válida.</summary>
public sealed record SyncPingResponse(bool Ok, DateTime ServerTimeUtc);
