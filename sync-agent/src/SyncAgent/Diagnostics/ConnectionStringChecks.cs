using Microsoft.Data.SqlClient;

namespace SyncAgent.Diagnostics;

/// <summary>Comprobaciones de la cadena de conexión que no necesitan abrir la conexión.</summary>
public static class ConnectionStringChecks
{
    /// <summary>Contraseña de <c>appsettings.Production.example.json</c>: significa "todavía sin configurar".</summary>
    public const string PlaceholderPassword = "CAMBIAR";

    /// <summary>
    /// ¿La contraseña es la de ejemplo? Así el agente no intenta iniciar sesión en SQL Server con una clave falsa (cada 15
    /// minutos, eso puede bloquear la cuenta de solo lectura). Una cadena que no se puede interpretar devuelve falso: de eso
    /// se ocupa la validación del formato.
    /// </summary>
    public static bool HasPlaceholderPassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            var password = new SqlConnectionStringBuilder(connectionString).Password;
            return string.Equals(password?.Trim(), PlaceholderPassword, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
    }
}
