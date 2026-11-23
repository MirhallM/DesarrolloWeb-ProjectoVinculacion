using backend.Data.Entities;

namespace backend.Catalog;

/// <summary>
/// Consultas y reglas compartidas del catálogo público.
/// Centraliza el predicado de visibilidad para garantizar consistencia entre
/// /api/products, /api/products/{id} y el conteo en /api/categories.
/// </summary>
public static class PublicCatalogQueries
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 100;
    public const decimal MaxPublicPrice = 100_000m;

    /// <summary>
    /// Filtra únicamente los productos visibles según docs/business-rules.md (sección 2):
    /// activo, 0 &lt; precio público &lt; 100 000 y nombre para mostrar no vacío.
    /// </summary>
    public static IQueryable<Product> WhereVisible(this IQueryable<Product> products) =>
        products.Where(p =>
            p.IsActive
            && p.PublicPrice > 0m
            && p.PublicPrice < MaxPublicPrice
            && p.DisplayName.Trim() != "");

    /// <summary>
    /// Aplica el ordenamiento cerrado permitido por US-08 (name o price, asc o desc)
    /// con desempate determinístico para mantener paginación estable.
    /// </summary>
    public static IQueryable<Product> ApplyPublicSort(
        this IQueryable<Product> products,
        string sort,
        bool descending) =>
        (sort, descending) switch
        {
            ("price", false) => products.OrderBy(p => p.PublicPrice).ThenBy(p => p.DisplayName).ThenBy(p => p.Id),
            ("price", true) => products.OrderByDescending(p => p.PublicPrice).ThenBy(p => p.DisplayName).ThenBy(p => p.Id),
            ("name", true) => products.OrderByDescending(p => p.DisplayName).ThenBy(p => p.Id),
            _ => products.OrderBy(p => p.DisplayName).ThenBy(p => p.Id),
        };

    /// <summary>
    /// Proyecta exclusivamente los campos públicos del producto haciendo LEFT JOIN con imágenes.
    /// </summary>
    public static IQueryable<PublicProductDto> ProjectToPublicDto(
        this IQueryable<Product> products,
        IQueryable<ProductImage> images) =>
        from p in products
        join img in images on p.Id equals img.ProductId into productImages
        from img in productImages.DefaultIfEmpty()
        select new PublicProductDto(
            p.Id,
            p.DisplayName,
            Math.Round(p.PublicPrice, 2),
            p.Stock > 0m,
            new PublicCategoryRefDto(p.Category.Slug, p.Category.Name),
            img != null && img.ImagePath != "" ? img.ImagePath : null);
}
