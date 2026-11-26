using System.Security.Claims;
using backend.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace backend.Auth;

/// <summary>
/// Guard para controladores o acciones exclusivas de administradores.
/// Responde 401 si no hay sesión válida y 403 si el usuario autenticado no posee el rol <c>Admin</c> (y el privilegio requerido).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminAttribute(string? requiredPermission = null) : Attribute, IAsyncAuthorizationFilter
{
    public string? RequiredPermission { get; } = requiredPermission;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            context.Result = ProblemResult(
                StatusCodes.Status401Unauthorized,
                AuthProblemTypes.Unauthenticated,
                "No autenticado",
                "Debes iniciar sesión para acceder a este recurso.");
            return;
        }

        if (!principal.IsInRole(AppRoleNames.Admin))
        {
            context.Result = ForbiddenResult();
            return;
        }

        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.Result = ProblemResult(
                StatusCodes.Status401Unauthorized,
                AuthProblemTypes.Unauthenticated,
                "No autenticado",
                "La sesión actual no es válida.");
            return;
        }

        // En operaciones administrativas se verifica el estado actual en PostgreSQL para aplicar bajas o revocaciones al instante.
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.Result = ProblemResult(
                StatusCodes.Status401Unauthorized,
                AuthProblemTypes.Unauthenticated,
                "No autenticado",
                "Debes iniciar sesión para acceder a este recurso.");
            return;
        }

        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Any(r => string.Equals(r, AppRoleNames.Admin, StringComparison.OrdinalIgnoreCase)))
        {
            context.Result = ForbiddenResult();
            return;
        }

        if (RequiredPermission is not null)
        {
            var permissions = AppPermissions.GetForRoles(roles);
            if (!permissions.Contains(RequiredPermission, StringComparer.Ordinal))
            {
                context.Result = ForbiddenResult();
            }
        }
    }

    private static ObjectResult ForbiddenResult() =>
        ProblemResult(
            StatusCodes.Status403Forbidden,
            AuthProblemTypes.Forbidden,
            "Acceso denegado",
            "Se requiere una cuenta con rol de administrador para realizar esta acción.");

    private static ObjectResult ProblemResult(int statusCode, string type, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Type = type,
            Title = title,
            Detail = detail,
        };

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}

/// <summary>Exige rol de administrador y un privilegio administrativo específico de <see cref="AppPermissions"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAdminPermissionAttribute(string permission) : RequireAdminAttribute(permission);
