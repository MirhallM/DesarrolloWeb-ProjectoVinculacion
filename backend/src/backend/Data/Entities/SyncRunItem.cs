namespace backend.Data.Entities;

/// <summary>
/// Artículo recibido en un lote de sincronización, guardado tal como llegó del agente (área de preparación).
/// Las reglas comerciales se aplican al completar la corrida; al terminar, estas filas se borran.
/// </summary>
public class SyncRunItem
{
    public Guid RunId { get; set; }

    /// <summary>Número del lote (desde 0) en que llegó el artículo.</summary>
    public int Sequence { get; set; }

    /// <summary>CVE_ART exacto, sin recortar.</summary>
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public double? Stock { get; set; }

    public string? Status { get; set; }

    public double? Price { get; set; }
}
