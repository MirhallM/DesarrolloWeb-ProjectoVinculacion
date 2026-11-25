namespace backend.Auth;

/// <summary>
/// Opciones de autenticación y seguridad de cuentas (US-13). Se enlazan desde la sección <c>Auth</c>.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Duración de la sesión por cookie con expiración deslizante (por defecto 7 días = 168 h).</summary>
    public int SessionExpireHours { get; set; } = 24 * 7;

    /// <summary>
    /// Cada cuántos minutos se revalida la cookie contra PostgreSQL (<c>SecurityStamp</c> e <c>IsActive</c>).
    /// </summary>
    public int SecurityStampValidationMinutes { get; set; } = 5;

    /// <summary>Intentos fallidos consecutivos antes de bloquear temporalmente la cuenta.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>Minutos que dura el bloqueo temporal tras superar <see cref="MaxFailedAccessAttempts"/>.</summary>
    public int LockoutMinutes { get; set; } = 15;
}
