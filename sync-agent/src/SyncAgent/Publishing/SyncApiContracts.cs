using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncAgent.Publishing;

// Mismos contratos que backend/src/backend/Sync/SyncContracts.cs (JSON en camelCase).

public sealed record StartSyncRunRequest(Guid RunId, int ItemCount, string? InventoryHash, string? AgentVersion);

public sealed record StartSyncRunResponse(Guid RunId, string Status, int? MaxBatchSize, DateTime? FinishedAt);

/// <summary>Un artículo tal como viene de Aspel: el código va exacto, sin recortar.</summary>
public sealed record SyncBatchItem(string Code, string? Description, double? Stock, string? Status, double? Price);

public sealed record SyncBatchRequest(int Sequence, IReadOnlyList<SyncBatchItem> Items);

public sealed record CompleteSyncRunRequest(int ItemCount, int BatchCount);

public sealed record SyncRunSummary(
    Guid RunId,
    string Status,
    int Read,
    int Inserted,
    int Updated,
    int Deactivated,
    int Rejected,
    IReadOnlyList<string>? RejectedCodes,
    DateTime? FinishedAt);

public sealed record AbortSyncRunRequest(string? Reason);

public sealed record SyncPingResponse(bool Ok, DateTime ServerTimeUtc);

/// <summary>Lo que el agente lee de un ProblemDetails de la API.</summary>
public sealed record ApiProblem(string? Type, string? Title, string? Detail, int? Status);

public static class SyncResponseStatuses
{
    public const string Running = "running";
    public const string Unchanged = "unchanged";
    public const string Succeeded = "succeeded";
}

/// <summary>Serialización sin reflexión (generada al compilar).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StartSyncRunRequest))]
[JsonSerializable(typeof(StartSyncRunResponse))]
[JsonSerializable(typeof(SyncBatchRequest))]
[JsonSerializable(typeof(CompleteSyncRunRequest))]
[JsonSerializable(typeof(SyncRunSummary))]
[JsonSerializable(typeof(AbortSyncRunRequest))]
[JsonSerializable(typeof(SyncPingResponse))]
[JsonSerializable(typeof(ApiProblem))]
internal sealed partial class SyncApiJsonContext : JsonSerializerContext;
