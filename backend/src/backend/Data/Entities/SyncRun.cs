namespace backend.Data.Entities;

/// <summary>
/// Registro de una ejecución de la sincronización con Aspel SAE.
/// </summary>
public class SyncRun
{
    public Guid Id { get; set; }

    /// <summary>Uno de los valores de <see cref="SyncRunStatuses"/>.</summary>
    public string Status { get; set; } = SyncRunStatuses.Running;

    /// <summary>Uno de los valores de <see cref="SyncRunModes"/>.</summary>
    public string Mode { get; set; } = SyncRunModes.Full;

    /// <summary>
    /// Huella (SHA-256 en hexadecimal) del inventario completo que calculó el agente. Permite registrar una corrida
    /// "sin cambios" sin volver a enviar los productos.
    /// </summary>
    public string? InventoryHash { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int ProductsRead { get; set; }

    public int ProductsInserted { get; set; }

    public int ProductsUpdated { get; set; }

    /// <summary>Productos activos que pasan a <c>is_active = false</c> porque dejaron de venir de Aspel.</summary>
    public int ProductsDeactivated { get; set; }

    /// <summary>Artículos recibidos que no se pudieron guardar (código vacío o valores fuera de rango).</summary>
    public int ProductsRejected { get; set; }

    /// <summary>
    /// Productos que ya existían y pasaron de activo a inactivo porque Aspel les puso <c>STATUS</c> distinto de 'A'.
    /// Es un subconjunto de <see cref="ProductsUpdated"/>. No incluye los que dejaron de venir (ver
    /// <see cref="ProductsDeactivated"/>). Solo informa: no frena la corrida.
    /// </summary>
    public int ProductsDeactivatedByStatus { get; set; }

    /// <summary>
    /// Huella de las reglas de catálogo con las que se aplicó la corrida (ver <c>CatalogRulesFingerprint</c>).
    /// NULL en las corridas anteriores a US-07 y en las que completó un contenedor con código anterior.
    /// </summary>
    public string? RulesFingerprint { get; set; }

    public string? ErrorMessage { get; set; }
}

public static class SyncRunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public static class SyncRunModes
{
    /// <summary>El agente envió el inventario completo y se aplicó.</summary>
    public const string Full = "full";

    /// <summary>La huella coincidió con la última corrida completa: no se envió ni cambió nada.</summary>
    public const string Unchanged = "unchanged";
}
