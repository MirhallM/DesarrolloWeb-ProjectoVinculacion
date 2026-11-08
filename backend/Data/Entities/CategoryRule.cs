namespace backend.Data.Entities;

/// <summary>
/// Regla de palabra clave para categorizar productos.
/// Si varias coinciden, gana la de mayor prioridad y,
/// si empatan, la palabra clave más larga.
/// </summary>
public class CategoryRule : IHasTimestamps
{
    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    /// <summary>
    /// Palabra clave. Única. Se guarda en mayúsculas y sin acentos,
    /// porque se compara contra la descripción normalizada de esa forma.
    /// </summary>
    public string Keyword { get; set; } = string.Empty;

    public int Priority { get; set; } = 0;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
