namespace backend.Data.Entities;

public class FeaturedProduct : IHasTimestamps
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>Orden en la página principal. Único entre los activos.</summary>
    public int Position { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
