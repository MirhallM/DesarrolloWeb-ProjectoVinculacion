namespace backend.Data.Entities;

/// <summary>
/// Registro de una ejecución de la sincronización con Aspel SAE.
/// </summary>
public class SyncRun
{
    public Guid Id { get; set; }

    /// <summary>Uno de los valores de <see cref="SyncRunStatuses"/>.</summary>
    public string Status { get; set; } = SyncRunStatuses.Running;

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int ProductsRead { get; set; }

    public int ProductsInserted { get; set; }

    public int ProductsUpdated { get; set; }

    public int ProductsDeactivated { get; set; }

    public string? ErrorMessage { get; set; }
}

public static class SyncRunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}
