using System.Net.Mail;
using backend.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace backend.Auth;

/// <summary>
/// Validación de datos de entrada y reglas de contraseñas para registro e inicio de sesión (US-13).
/// </summary>
public static class AuthValidation
{
    /// <summary>
    /// Normaliza el correo (recorta espacios extremos y pasa a minúsculas) y verifica que tenga formato válido.
    /// </summary>
    public static bool TryNormalizeEmail(string? rawEmail, out string normalizedEmail, out string error)
    {
        normalizedEmail = string.Empty;

        if (string.IsNullOrWhiteSpace(rawEmail))
        {
            error = "El correo electrónico es obligatorio.";
            return false;
        }

        var trimmed = rawEmail.Trim();
        if (trimmed.Length > AuthConstants.MaxEmailLength)
        {
            error = $"El correo electrónico no puede superar los {AuthConstants.MaxEmailLength} caracteres.";
            return false;
        }

        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            error = "El correo electrónico no tiene un formato válido.";
            return false;
        }

        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 0 || atIndex != trimmed.LastIndexOf('@') || atIndex == trimmed.Length - 1)
        {
            error = "El correo electrónico no tiene un formato válido.";
            return false;
        }

        var domain = trimmed[(atIndex + 1)..];
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains(".."))
        {
            error = "El correo electrónico no tiene un formato válido.";
            return false;
        }

        if (!MailAddress.TryCreate(trimmed, out var parsed) || !string.Equals(parsed.Address, trimmed, StringComparison.Ordinal))
        {
            error = "El correo electrónico no tiene un formato válido.";
            return false;
        }

        normalizedEmail = trimmed.ToLowerInvariant();
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Valida los campos de <c>POST /api/auth/register</c>. La contraseña NO se recorta para respetar espacios intencionales.
    /// </summary>
    public static Dictionary<string, List<string>> ValidateRegister(RegisterRequest? request, out string normalizedEmail)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        normalizedEmail = string.Empty;

        if (request is null)
        {
            AddError(errors, "email", "El correo electrónico es obligatorio.");
            AddError(errors, "password", "La contraseña es obligatoria.");
            return errors;
        }

        if (!TryNormalizeEmail(request.Email, out normalizedEmail, out var emailError))
        {
            AddError(errors, "email", emailError);
        }

        var password = request.Password;
        if (string.IsNullOrEmpty(password))
        {
            AddError(errors, "password", "La contraseña es obligatoria.");
            return errors;
        }

        if (password.Length < AuthConstants.MinPasswordLength)
        {
            AddError(errors, "password", $"La contraseña debe tener al menos {AuthConstants.MinPasswordLength} caracteres.");
        }

        if (password.Length > AuthConstants.MaxPasswordLength)
        {
            AddError(errors, "password", $"La contraseña no puede superar los {AuthConstants.MaxPasswordLength} caracteres.");
        }

        if (!password.Any(char.IsLetter))
        {
            AddError(errors, "password", "La contraseña debe incluir al menos una letra.");
        }

        if (!password.Any(char.IsDigit))
        {
            AddError(errors, "password", "La contraseña debe incluir al menos un número.");
        }

        if (password.Distinct().Count() < AuthConstants.MinPasswordUniqueChars)
        {
            AddError(errors, "password", $"La contraseña debe contener al menos {AuthConstants.MinPasswordUniqueChars} caracteres distintos.");
        }

        return errors;
    }

    /// <summary>
    /// Valida el formato básico de <c>POST /api/auth/login</c> sin exponer las reglas internas de complejidad.
    /// </summary>
    public static Dictionary<string, List<string>> ValidateLogin(LoginRequest? request, out string normalizedEmail)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        normalizedEmail = string.Empty;

        if (request is null)
        {
            AddError(errors, "email", "El correo electrónico es obligatorio.");
            AddError(errors, "password", "La contraseña es obligatoria.");
            return errors;
        }

        if (!TryNormalizeEmail(request.Email, out normalizedEmail, out var emailError))
        {
            AddError(errors, "email", emailError);
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            AddError(errors, "password", "La contraseña es obligatoria.");
        }
        else if (request.Password.Length > AuthConstants.MaxPasswordLength)
        {
            AddError(errors, "password", $"La contraseña no puede superar los {AuthConstants.MaxPasswordLength} caracteres.");
        }

        return errors;
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = [];
            errors[key] = list;
        }

        list.Add(message);
    }
}

/// <summary>
/// Exige al menos una letra en Identity (complementa <c>RequireDigit</c> sin obligar a distinguir mayúsculas/minúsculas).
/// </summary>
public sealed class LetterRequiredPasswordValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (string.IsNullOrEmpty(password) || !password.Any(char.IsLetter))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresLetter",
                Description = "La contraseña debe incluir al menos una letra.",
            }));
        }

        if (password.Length > AuthConstants.MaxPasswordLength)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"La contraseña no puede superar los {AuthConstants.MaxPasswordLength} caracteres.",
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}

/// <summary>Mensajes en español para errores emitidos directamente por ASP.NET Core Identity.</summary>
public sealed class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"La contraseña debe tener al menos {length} caracteres." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "La contraseña debe incluir al menos un número." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"La contraseña debe contener al menos {uniqueChars} caracteres distintos." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Ya existe una cuenta registrada con ese correo electrónico." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Ya existe una cuenta registrada con ese correo electrónico." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "El correo electrónico no tiene un formato válido." };
}
