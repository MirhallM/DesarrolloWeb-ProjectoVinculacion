namespace backend.Auth;

public sealed record RegisterRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password, bool RememberMe = false);

public sealed record AuthUserResponse(Guid Id, string Email, IReadOnlyList<string> Roles);

public static class AuthProblemTypes
{
    public const string InvalidCredentials = "auth-invalid-credentials";
    public const string DuplicateEmail = "auth-duplicate-email";
    public const string AccountLocked = "auth-account-locked";
    public const string Unauthenticated = "auth-unauthenticated";
    public const string Forbidden = "auth-forbidden";
    public const string CsrfRejected = "auth-csrf-rejected";
}
