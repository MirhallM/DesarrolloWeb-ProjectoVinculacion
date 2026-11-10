using Microsoft.Data.SqlClient;

namespace SyncAgent.Diagnostics;

/// <summary>
/// Sustituye por <c>***</c> los secretos conocidos (usuario, contraseña y la cadena de conexión completa)
/// en cualquier texto antes de que llegue a un log o a la pantalla.
/// </summary>
/// <remarks>
/// Es la última línea de defensa: el resto del código ya evita registrar excepciones crudas de SqlClient,
/// pero el mensaje de un login fallido incluye el nombre de usuario ("Login failed for user '...'").
/// </remarks>
public sealed class SecretRedactor
{
    public const string Mask = "***";

    private readonly string[] _secrets;

    public SecretRedactor(IEnumerable<string?> secrets)
    {
        // Los más largos primero, para que la cadena completa se sustituya antes que sus fragmentos.
        _secrets = secrets
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length)
            .ToArray();
    }

    /// <summary>Redactor que no conoce ningún secreto (solo aplica el patrón genérico de login fallido).</summary>
    public static SecretRedactor None { get; } = new([]);

    /// <summary>Crea el redactor a partir de la cadena de conexión. Si la cadena no se puede interpretar, solo oculta la cadena entera.</summary>
    public static SecretRedactor FromConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return None;

        var secrets = new List<string?> { connectionString };
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            secrets.Add(builder.Password);
            secrets.Add(builder.UserID);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Cadena mal formada: nos quedamos con la redacción de la cadena completa.
        }

        return new SecretRedactor(secrets);
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var result = text;
        foreach (var secret in _secrets)
        {
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
        }

        // Patrón genérico de SQL Server, aunque el usuario no sea el configurado.
        result = LoginFailedPattern.Replace(result, $"for user '{Mask}'");
        return result;
    }

    private static readonly System.Text.RegularExpressions.Regex LoginFailedPattern =
        new(@"for user '[^']*'", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
}
