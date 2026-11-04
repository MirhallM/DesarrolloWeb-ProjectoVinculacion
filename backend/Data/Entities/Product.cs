namespace backend.Data.Entities;

/// <summary>
/// Copia sincronizada de un artículo de Aspel SAE.
/// Solo la modifica el proceso de sincronización.
/// </summary>
public class Product : IHasTimestamps
{
    public Guid Id { get; set; }

    /// <summary>Código de artículo de Aspel (CVE_ART). Único.</summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? CategoryCode { get; set; }

    public string? CategoryName { get; set; }

    public decimal PublicPrice { get; set; }

    /// <summary>Existencia. Puede ser negativa en Aspel.</summary>
    public decimal Stock { get; set; }

    /// <summary>false cuando el producto deja de venir de Aspel.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? LastPurchaseAt { get; set; }

    public DateTime? LastSaleAt { get; set; }

    public DateTime SyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
