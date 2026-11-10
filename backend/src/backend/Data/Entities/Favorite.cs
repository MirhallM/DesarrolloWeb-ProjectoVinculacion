namespace backend.Data.Entities;

public class Favorite
{
    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
