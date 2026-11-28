using backend.Data;
using backend.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace backend.Auth;

public enum AdminOperationStatus
{
    Succeeded,
    ValidationFailed,
    UserNotFound,
    UserInactive,
}

public sealed record AdminOperationResult(AdminOperationStatus Status, string Message);

/// <summary>
/// Asigna de forma controlada el rol <c>Admin</c> a una cuenta ya registrada (solo invocable por consola).
/// </summary>
public sealed class AdminRoleService(AppDbContext db, UserManager<AppUser> userManager)
{
    public async Task<AdminOperationResult> PromoteToAdminAsync(string? email, CancellationToken ct = default)
    {
        if (!AuthValidation.TryNormalizeEmail(email, out var normalizedEmail, out var emailError))
        {
            return new AdminOperationResult(AdminOperationStatus.ValidationFailed, emailError);
        }

        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            return new AdminOperationResult(
                AdminOperationStatus.UserNotFound,
                $"No existe ninguna cuenta registrada con el correo '{normalizedEmail}'.");
        }

        if (!user.IsActive)
        {
            return new AdminOperationResult(
                AdminOperationStatus.UserInactive,
                $"La cuenta '{normalizedEmail}' está desactivada y no puede recibir el rol Admin.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var alreadyAdmin = await userManager.IsInRoleAsync(user, AppRoleNames.Admin);
        if (!alreadyAdmin)
        {
            foreach (var role in new[] { AppRoleNames.User, AppRoleNames.Admin })
            {
                if (await userManager.IsInRoleAsync(user, role))
                {
                    continue;
                }

                var result = await userManager.AddToRoleAsync(user, role);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"No se pudo asignar el rol '{role}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }

            await userManager.UpdateSecurityStampAsync(user);
        }

        await transaction.CommitAsync(ct);

        var message = alreadyAdmin
            ? $"La cuenta '{normalizedEmail}' ya tenía el rol Admin."
            : $"Rol Admin asignado a '{normalizedEmail}'.";

        return new AdminOperationResult(AdminOperationStatus.Succeeded, message);
    }
}
