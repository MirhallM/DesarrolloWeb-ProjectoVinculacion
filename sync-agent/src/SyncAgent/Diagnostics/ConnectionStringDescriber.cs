using Microsoft.Data.SqlClient;

namespace SyncAgent.Diagnostics;

/// <summary>
/// Describe una cadena de conexión para los logs: solo servidor y base de datos.
/// Nunca incluye el usuario ni la contraseña ni el resto de la cadena.
/// </summary>
public static class ConnectionStringDescriber
{
    public static string Describe(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "(cadena de conexión sin configurar)";

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return $"servidor={Or(builder.DataSource)}, base={Or(builder.InitialCatalog)}";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // No se repite ni el mensaje de la excepción ni la cadena: podrían traer credenciales.
            return "(cadena de conexión con formato inválido)";
        }
    }

    private static string Or(string value) => string.IsNullOrWhiteSpace(value) ? "(no indicado)" : value;
}
