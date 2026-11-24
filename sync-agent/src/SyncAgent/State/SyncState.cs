namespace SyncAgent.State;

/// <summary>
/// Estado de la última sincronización. Se guarda en disco (<c>data/sync-state.json</c>) porque el agente
/// corre como tarea programada: cada ejecución es un proceso nuevo y no recuerda nada de la anterior.
/// </summary>
public sealed class SyncState
{
    public DateTimeOffset? LastRunStartedAt { get; set; }
    public DateTimeOffset? LastRunFinishedAt { get; set; }

    /// <summary>Uno de los valores de <see cref="SyncResults"/>.</summary>
    public string? LastResult { get; set; }

    /// <summary>Mensaje del último fallo, ya sin usuario ni contraseña. Nulo si la última ejecución fue correcta.</summary>
    public string? LastError { get; set; }

    /// <summary>Última vez que una sincronización terminó bien. NO se borra cuando una ejecución posterior falla.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Fallos seguidos desde la última ejecución correcta.</summary>
    public int ConsecutiveFailures { get; set; }

    public int ProductsRead { get; set; }
    public long QueryDurationMs { get; set; }
    public double PeakWorkingSetMb { get; set; }

    /// <summary>Id de la última corrida que la API confirmó.</summary>
    public Guid? LastRunId { get; set; }

    /// <summary>Huella del inventario enviado en la última corrida confirmada. Solo diagnóstico: la API decide si hay cambios.</summary>
    public string? LastInventoryHash { get; set; }

    /// <summary>Resumen de la API de la última corrida confirmada.</summary>
    public PublishSummary? LastPublish { get; set; }
}

/// <summary>Lo que la API informó al confirmar el envío.</summary>
public sealed class PublishSummary
{
    /// <summary><c>full</c> (se envió y aplicó el inventario) o <c>unchanged</c> (sin cambios: no se envió nada).</summary>
    public string Mode { get; set; } = "full";

    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deactivated { get; set; }
    public int Rejected { get; set; }
}

public static class SyncResults
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}
