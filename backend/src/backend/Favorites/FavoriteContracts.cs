using backend.Catalog;

namespace backend.Favorites;

public static class FavoriteAvailabilityStatuses
{
    public const string Available = "available";
    public const string OutOfStock = "out_of_stock";
    public const string Unavailable = "unavailable";
}

public static class FavoriteProblemTypes
{
    public const string ProductNotFound = "favorite-product-not-found";
    public const string AlreadyExists = "favorite-already-exists";
}

/// <summary>
/// Producto guardado en la lista de favoritos del usuario autenticado.
/// Nunca expone código interno (CVE_ART), descripción cruda de Aspel ni cantidad numérica en existencia.
/// Si el producto quedó oculto después de guardarse, <see cref="IsVisible"/> es <c>false</c>,
/// <see cref="AvailabilityStatus"/> es <c>unavailable</c> y <see cref="Price"/> es <c>null</c>.
/// </summary>
public sealed record FavoriteItemDto(
    Guid ProductId,
    string Name,
    decimal? Price,
    bool IsVisible,
    bool Available,
    string AvailabilityStatus,
    PublicCategoryRefDto Category,
    string? ImageUrl,
    DateTime SavedAt);
