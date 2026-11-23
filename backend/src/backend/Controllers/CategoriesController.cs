using backend.Catalog;
using backend.Data;
using backend.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

/// <summary>
/// Endpoints públicos de consulta de categorías del catálogo (US-08).
/// </summary>
[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Lista de categorías ordenadas por posición (con "Otros" al final) y su conteo de productos visibles.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PublicCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PublicCategoryDto>>> GetCategories(CancellationToken ct = default)
    {
        var visibleProducts = db.Products.AsNoTracking().WhereVisible();

        var categories = await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Id == Category.OthersId ? 1 : 0)
            .ThenBy(c => c.Position)
            .ThenBy(c => c.Name)
            .Select(c => new PublicCategoryDto(
                c.Slug,
                c.Name,
                visibleProducts.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(ct);

        return Ok(categories);
    }
}
