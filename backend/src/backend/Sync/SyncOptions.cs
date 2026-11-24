namespace backend.Sync;

/// <summary>Sección <c>Sync</c> de la configuración (en Railway: variables <c>Sync__*</c>).</summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// Hash SHA-256 (hexadecimal) de la llave del agente. Se admiten dos separados por coma para rotar la llave
    /// sin perder un ciclo. La llave en sí nunca se configura en el backend.
    /// </summary>
    public string? AgentKeyHashes { get; set; }

    /// <summary>Máximo de artículos por lote.</summary>
    public int MaxBatchSize { get; set; } = 2000;

    /// <summary>Una corrida en <c>running</c> con más de estos minutos se considera abandonada.</summary>
    public int AbandonAfterMinutes { get; set; } = 15;

    /// <summary>
    /// Una corrida "sin cambios" solo se acepta si la última corrida completa tiene menos de estas horas.
    /// Así hay al menos un envío completo al día aunque el inventario no cambie.
    /// </summary>
    public int FullRunMaxAgeHours { get; set; } = 24;

    /// <summary>Freno: fracción máxima de los activos que una corrida puede desactivar por ausencia.</summary>
    public double MaxDeactivationRatio { get; set; } = 0.10;

    /// <summary>Freno: por debajo de esta cantidad de desactivaciones no se frena, sea cual sea la fracción.</summary>
    public int MaxDeactivationCount { get; set; } = 20;

    /// <summary>Días que se conservan las corridas "sin cambios" en <c>sync_runs</c>.</summary>
    public int UnchangedRunRetentionDays { get; set; } = 30;
}
