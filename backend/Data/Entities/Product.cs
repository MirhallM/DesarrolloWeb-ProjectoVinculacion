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

    /// <summary>Descripción original de Aspel. No se muestra al cliente.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Nombre para mostrar al cliente, generado a partir de la descripción.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public decimal PublicPrice { get; set; }

    /// <summary>Existencia. Puede ser negativa en Aspel.</summary>
    public decimal Stock { get; set; }

    /// <summary>false cuando el producto deja de venir de Aspel.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime SyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
