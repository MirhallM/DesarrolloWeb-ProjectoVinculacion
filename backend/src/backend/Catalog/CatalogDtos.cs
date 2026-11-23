namespace backend.Catalog;

/// <summary>Referencia pública de categoría incluida en el detalle/listado de productos.</summary>
public sealed record PublicCategoryRefDto(string Slug, string Name);

/// <summary>
/// DTO público de producto para el catálogo (US-08).
/// Excluye deliberadamente código (CVE_ART), descripción original de Aspel y cantidad en existencia.
/// </summary>
public sealed record PublicProductDto(
    Guid Id,
    string Name,
    decimal Price,
    bool Available,
    PublicCategoryRefDto Category,
    string? ImageUrl);

/// <summary>DTO público de categoría con el conteo de productos visibles.</summary>
public sealed record PublicCategoryDto(
    string Slug,
    string Name,
    int ProductCount);

/// <summary>Respuesta paginada estándar de listados (docs/architecture.md).</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
