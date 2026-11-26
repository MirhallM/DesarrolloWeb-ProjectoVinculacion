using backend.Auth;
using backend.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/admin")]
[RequireAdminPermission(AppPermissions.AdminAccess)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminController(UserManager<AppUser> userManager) : ControllerBase
{
    /// <summary>
    /// Verifica en el backend que la sesión activa pertenece a un administrador y devuelve sus privilegios habilitados.
    /// </summary>
    [HttpGet("access")]
    [ProducesResponseType<AuthUserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccess()
    {
        Response.Headers.CacheControl = "no-store";

        var user = (await userManager.GetUserAsync(User))!;
        var roles = (await userManager.GetRolesAsync(user))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        return Ok(new AuthUserResponse(
            user.Id,
            user.Email!,
            roles,
            AppPermissions.GetForRoles(roles)));
    }
}
