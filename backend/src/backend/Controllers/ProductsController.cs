using backend.Catalog;
using backend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

/// <summary>
/// Endpoints públicos de consulta del catálogo de productos (US-08).
/// </summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Lista paginada de productos visibles del catálogo.
    /// </summary>
    /// <param name="page">Número de página (>= 1). Por defecto 1.</param>
    /// <param name="pageSize">Cantidad por página (1 a 100). Por defecto 24.</param>
    /// <param name="sort">Campo de ordenamiento permitido: name (por defecto) o price.</param>
    /// <param name="order">Dirección de ordenamiento permitida: asc (por defecto) o desc.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<PublicProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] int page = PublicCatalogQueries.DefaultPage,
        [FromQuery] int pageSize = PublicCatalogQueries.DefaultPageSize,
        [FromQuery] string? sort = null,
        [FromQuery] string? order = null,
        CancellationToken ct = default)
    {
        if (page < 1)
        {
            ModelState.AddModelError(nameof(page), "El parámetro 'page' debe ser mayor o igual a 1.");
        }

        if (pageSize < 1 || pageSize > PublicCatalogQueries.MaxPageSize)
        {
            ModelState.AddModelError(
                nameof(pageSize),
                $"El parámetro 'pageSize' debe estar entre 1 y {PublicCatalogQueries.MaxPageSize}.");
        }

        // Si el cliente envía ?sort= vacío, MVC enlaza null; se distingue de omitir el parámetro usando Request.Query.
        var effectiveSort = sort ?? (Request.Query.ContainsKey(nameof(sort)) ? string.Empty : "name");
        var normalizedSort = effectiveSort.Trim().ToLowerInvariant();
        if (normalizedSort is not ("name" or "price"))
        {
            ModelState.AddModelError(nameof(sort), "El parámetro 'sort' solo admite 'name' o 'price'.");
        }

        var effectiveOrder = order ?? (Request.Query.ContainsKey(nameof(order)) ? string.Empty : "asc");
        var normalizedOrder = effectiveOrder.Trim().ToLowerInvariant();
        if (normalizedOrder is not ("asc" or "desc"))
        {
            ModelState.AddModelError(nameof(order), "El parámetro 'order' solo admite 'asc' o 'desc'.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var visibleProducts = db.Products.AsNoTracking().WhereVisible();

        var totalCount = await visibleProducts.CountAsync(ct);

        var items = await visibleProducts
            .ApplyPublicSort(normalizedSort, descending: normalizedOrder == "desc")
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ProjectToPublicDto(db.ProductImages.AsNoTracking())
            .ToListAsync(ct);

        return Ok(new PagedResult<PublicProductDto>(items, page, pageSize, totalCount));
    }

    /// <summary>
    /// Detalle público de un producto visible por su Id.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PublicProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken ct = default)
    {
        var product = await db.Products
            .AsNoTracking()
            .WhereVisible()
            .Where(p => p.Id == id)
            .ProjectToPublicDto(db.ProductImages.AsNoTracking())
            .SingleOrDefaultAsync(ct);

        if (product is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Producto no encontrado",
                detail: $"No existe un producto visible con id '{id}'.");
        }

        return Ok(product);
    }
}
