namespace backend.Data.Entities;

/// <summary>
/// Entidades con fecha de creación y de última modificación.
/// AppDbContext las llena automáticamente al guardar.
/// </summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }

    DateTime UpdatedAt { get; set; }
}
