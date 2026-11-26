using backend.Auth;
using backend.Data;
using backend.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IPasswordHasher<AppUser> passwordHasher) : ControllerBase
{
    private const string UniqueViolationSqlState = "23505";

    // Hash ficticio para igualar el tiempo de cómputo cuando el correo no existe.
    private static readonly AppUser DummyUser = new() { Id = Guid.Empty, Email = "dummy@nettes.local", UserName = "dummy@nettes.local" };
    private readonly Lazy<string> _dummyPasswordHash = new(
        () => passwordHasher.HashPassword(DummyUser, "ClaveFicticiaDeTiempoConstante1234"));

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        var validationErrors = AuthValidation.ValidateRegister(request, out var normalizedEmail);
        if (validationErrors.Count > 0)
        {
            return ToValidationProblem(validationErrors);
        }

        if (await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            return DuplicateEmailProblem();
        }

        var user = new AppUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            IsActive = true,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user, request.Password!);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            return DuplicateEmailProblem();
        }

        if (!createResult.Succeeded)
        {
            if (createResult.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                    or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                return DuplicateEmailProblem();
            }

            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError("password", error.Description);
            }

            return ValidationProblem(ModelState);
        }

        var roleResult = await userManager.AddToRoleAsync(user, AppRoleNames.User);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo asignar el rol '{AppRoleNames.User}': {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        }

        await transaction.CommitAsync(ct);

        await signInManager.SignInAsync(user, isPersistent: false);

        string[] roles = [AppRoleNames.User];
        var response = new AuthUserResponse(user.Id, user.Email!, roles, AppPermissions.GetForRoles(roles));
        return CreatedAtAction(nameof(Me), response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        Response.Headers.CacheControl = "no-store";

        var validationErrors = AuthValidation.ValidateLogin(request, out var normalizedEmail);
        if (validationErrors.Count > 0)
        {
            return ToValidationProblem(validationErrors);
        }

        var password = request.Password!;
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            _ = passwordHasher.VerifyHashedPassword(DummyUser, _dummyPasswordHash.Value, password);
            return InvalidCredentialsProblem();
        }

        if (!user.IsActive)
        {
            _ = passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? _dummyPasswordHash.Value, password);
            return InvalidCredentialsProblem();
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (signInResult.IsLockedOut)
        {
            return Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                type: AuthProblemTypes.AccountLocked,
                title: "Cuenta bloqueada temporalmente",
                detail: "Se superó el número máximo de intentos fallidos. Intenta nuevamente más tarde.");
        }

        if (!signInResult.Succeeded)
        {
            return InvalidCredentialsProblem();
        }

        await signInManager.SignInAsync(user, isPersistent: request.RememberMe);
        var roles = (await userManager.GetRolesAsync(user))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        return Ok(new AuthUserResponse(user.Id, user.Email!, roles, AppPermissions.GetForRoles(roles)));
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        Response.Headers.CacheControl = "no-store";
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<AuthUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        Response.Headers.CacheControl = "no-store";

        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            await signInManager.SignOutAsync();
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                type: AuthProblemTypes.Unauthenticated,
                title: "No autenticado",
                detail: "Debes iniciar sesión para acceder a este recurso.");
        }

        var roles = (await userManager.GetRolesAsync(user))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();
        return Ok(new AuthUserResponse(user.Id, user.Email!, roles, AppPermissions.GetForRoles(roles)));
    }

    private ObjectResult InvalidCredentialsProblem() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            type: AuthProblemTypes.InvalidCredentials,
            title: "Credenciales inválidas",
            detail: "El correo electrónico o la contraseña son incorrectos.");

    private ObjectResult DuplicateEmailProblem() =>
        Problem(
            statusCode: StatusCodes.Status409Conflict,
            type: AuthProblemTypes.DuplicateEmail,
            title: "Correo ya registrado",
            detail: "Ya existe una cuenta registrada con ese correo electrónico.");

    private ActionResult ToValidationProblem(Dictionary<string, List<string>> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }

        return ValidationProblem(ModelState);
    }
}
