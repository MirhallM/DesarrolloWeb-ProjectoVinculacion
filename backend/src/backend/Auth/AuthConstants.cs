namespace backend.Auth;

public static class AuthConstants
{
    // __Host- exige HTTPS, Path=/ y ausencia de Domain; en desarrollo se omite para http://localhost.
    public const string DevAuthCookieName = "nettes.auth";
    public const string ProdAuthCookieName = "__Host-nettes.auth";

    public const int MaxEmailLength = 256;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MinPasswordUniqueChars = 4;
}
