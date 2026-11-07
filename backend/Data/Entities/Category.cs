namespace backend.Data.Entities;

/// <summary>
/// Categoría propia del catálogo. Aspel no tiene categorías útiles,
/// así que la aplicación las asigna con reglas de palabras clave.
/// </summary>
public class Category : IHasTimestamps
{
    /// <summary>
    /// Id fijo de la categoría "Otros", sembrada en la base de datos.
    /// Recibe los productos que no coinciden con ninguna regla.
    /// </summary>
    public static readonly Guid OthersId = new("3c9a7e52-8d14-4f6b-b2a7-5e1f0c6d9a01");

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    /// <summary>Orden en el catálogo. Empieza en 1.</summary>
    public int Position { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
