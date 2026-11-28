using System.Security.Claims;
using backend.Auth;
using backend.Data.Entities;
using backend.Favorites;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/favorites")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class FavoritesController(
    FavoritesService favorites,
    UserManager<AppUser> userManager) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FavoriteItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFavorites(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = await ResolveActiveUserIdAsync();
        if (userId is null)
        {
            return UnauthenticatedProblem();
        }

        var items = await favorites.ListForUserAsync(userId.Value, ct);
        return Ok(items);
    }

    [HttpPost("{productId:guid}")]
    [ProducesResponseType<FavoriteItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddFavorite(Guid productId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = await ResolveActiveUserIdAsync();
        if (userId is null)
        {
            return UnauthenticatedProblem();
        }

        var result = await favorites.AddAsync(userId.Value, productId, ct);
        return result.Status switch
        {
            AddFavoriteStatus.Created => CreatedAtAction(nameof(GetFavorites), result.Item),
            AddFavoriteStatus.AlreadyExists => Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: FavoriteProblemTypes.AlreadyExists,
                title: "Favorito duplicado",
                detail: "Este producto ya se encuentra en tu lista de favoritos."),
            _ => Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: FavoriteProblemTypes.ProductNotFound,
                title: "Producto no disponible",
                detail: "El producto no existe o no está disponible en el catálogo público."),
        };
    }

    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveFavorite(Guid productId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = await ResolveActiveUserIdAsync();
        if (userId is null)
        {
            return UnauthenticatedProblem();
        }

        await favorites.RemoveAsync(userId.Value, productId, ct);
        return NoContent();
    }

    private async Task<Guid?> ResolveActiveUserIdAsync()
    {
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawId, out var userId))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return null;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return null;
        }

        return user.Id;
    }

    private ObjectResult UnauthenticatedProblem() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            type: AuthProblemTypes.Unauthenticated,
            title: "No autenticado",
            detail: "Debes iniciar sesión para gestionar tus productos favoritos.");
}
