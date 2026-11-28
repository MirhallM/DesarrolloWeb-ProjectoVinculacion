using backend.Catalog;
using backend.Data;
using backend.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend.Favorites;

public enum AddFavoriteStatus
{
    Created,
    AlreadyExists,
    ProductNotFoundOrHidden,
}

public sealed record AddFavoriteResult(AddFavoriteStatus Status, FavoriteItemDto? Item = null);

public sealed class FavoritesService(AppDbContext db)
{
    private const string FallbackUnavailableName = "Producto no disponible";
    private const string UniqueViolationSqlState = "23505";

    public async Task<IReadOnlyList<FavoriteItemDto>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await (
            from fav in db.Favorites.AsNoTracking()
            where fav.UserId == userId
            join product in db.Products.AsNoTracking() on fav.ProductId equals product.Id
            join category in db.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join img in db.ProductImages.AsNoTracking() on product.Id equals img.ProductId into productImages
            from img in productImages.DefaultIfEmpty()
            orderby fav.CreatedAt descending, product.Id
            select new RawFavoriteRow(
                product.Id,
                product.DisplayName,
                product.PublicPrice,
                product.Stock,
                product.IsActive,
                category.Slug,
                category.Name,
                img != null && img.ImagePath != "" ? img.ImagePath : null,
                fav.CreatedAt))
            .ToListAsync(ct);

        return rows.Select(MapToDto).ToList();
    }

    public async Task<AddFavoriteResult> AddAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        var visibleProduct = await (
            from product in db.Products.AsNoTracking().WhereVisible()
            where product.Id == productId
            join category in db.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join img in db.ProductImages.AsNoTracking() on product.Id equals img.ProductId into productImages
            from img in productImages.DefaultIfEmpty()
            select new
            {
                product.Id,
                product.DisplayName,
                product.PublicPrice,
                product.Stock,
                CategorySlug = category.Slug,
                CategoryName = category.Name,
                ImageUrl = img != null && img.ImagePath != "" ? img.ImagePath : null,
            })
            .SingleOrDefaultAsync(ct);

        if (visibleProduct is null)
        {
            return new AddFavoriteResult(AddFavoriteStatus.ProductNotFoundOrHidden);
        }

        var exists = await db.Favorites
            .AsNoTracking()
            .AnyAsync(f => f.UserId == userId && f.ProductId == productId, ct);

        if (exists)
        {
            return new AddFavoriteResult(AddFavoriteStatus.AlreadyExists);
        }

        var favorite = new Favorite
        {
            UserId = userId,
            ProductId = productId,
        };

        db.Favorites.Add(favorite);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            db.Entry(favorite).State = EntityState.Detached;
            return new AddFavoriteResult(AddFavoriteStatus.AlreadyExists);
        }

        var dto = MapToDto(new RawFavoriteRow(
            visibleProduct.Id,
            visibleProduct.DisplayName,
            visibleProduct.PublicPrice,
            visibleProduct.Stock,
            IsActive: true,
            visibleProduct.CategorySlug,
            visibleProduct.CategoryName,
            visibleProduct.ImageUrl,
            favorite.CreatedAt));

        return new AddFavoriteResult(AddFavoriteStatus.Created, dto);
    }

    public async Task RemoveAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        await db.Favorites
            .Where(f => f.UserId == userId && f.ProductId == productId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Proyecta una fila de favorito aplicando las reglas comerciales de visibilidad (docs/business-rules.md):
    /// si el producto quedó oculto posteriormente, se reporta como <c>unavailable</c> y sin precio.
    /// </summary>
    public static FavoriteItemDto MapToDto(RawFavoriteRow row)
    {
        var trimmedName = row.DisplayName.Trim();
        var isVisible = row.IsActive
            && row.PublicPrice > 0m
            && row.PublicPrice < PublicCatalogQueries.MaxPublicPrice
            && trimmedName.Length > 0;

        var safeName = trimmedName.Length > 0 ? row.DisplayName : FallbackUnavailableName;
        var available = isVisible && row.Stock > 0m;
        var status = !isVisible
            ? FavoriteAvailabilityStatuses.Unavailable
            : available
                ? FavoriteAvailabilityStatuses.Available
                : FavoriteAvailabilityStatuses.OutOfStock;

        return new FavoriteItemDto(
            row.ProductId,
            safeName,
            isVisible ? Math.Round(row.PublicPrice, 2) : null,
            isVisible,
            available,
            status,
            new PublicCategoryRefDto(row.CategorySlug, row.CategoryName),
            row.ImageUrl,
            row.SavedAt);
    }

    public sealed record RawFavoriteRow(
        Guid ProductId,
        string DisplayName,
        decimal PublicPrice,
        decimal Stock,
        bool IsActive,
        string CategorySlug,
        string CategoryName,
        string? ImageUrl,
        DateTime SavedAt);
}
