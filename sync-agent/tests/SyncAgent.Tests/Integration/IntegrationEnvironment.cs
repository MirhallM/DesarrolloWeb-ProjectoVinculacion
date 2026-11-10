using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace SyncAgent.Tests.Integration;

/// <summary>
/// Datos para las pruebas que usan el SQL Server de Docker (docker compose --profile sqlserver up -d).
/// Sin la variable SYNCAGENT_TEST_CONNECTION esas pruebas se omiten, así que el CI y quien no tenga la base
/// de prueba pueden correr el resto sin problema.
/// </summary>
public static class IntegrationEnvironment
{
    public const string VariableName = "SYNCAGENT_TEST_CONNECTION";

    public const string SkipReason =
        "Define la variable de entorno SYNCAGENT_TEST_CONNECTION con la cadena de la base de prueba (ver sync-agent/README.md).";

    /// <summary>Cadena de conexión de la cuenta catalogo_lectura a la base de PRUEBA.</summary>
    public static string? ConnectionString => Environment.GetEnvironmentVariable(VariableName);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    // La comprobación se hace una sola vez por ejecución de las pruebas.
    private static readonly Lazy<Task> TestDatabaseCheck = new(VerifyIsTestDatabaseAsync);

    /// <summary>
    /// Se llama al inicio de cada prueba de integración. Si la base NO es la de prueba sembrada por
    /// sql/dev/02-datos-prueba.sql, lanza una excepción y no se ejecuta nada más. Es una protección: algunas
    /// pruebas intentan escribir (para comprobar que falla) y nunca deben correr contra la base real de Aspel.
    /// </summary>
    public static Task EnsureTestDatabaseAsync() => TestDatabaseCheck.Value;

    private static async Task VerifyIsTestDatabaseAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT COUNT(*) FROM dbo.INVE01), " +
            "       (SELECT COUNT(*) FROM dbo.INVE01 WHERE CVE_ART = '7501000100011' AND DESCR = 'LECHE ENT 1LT')";

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var total = reader.GetInt32(0);
        var sentinel = reader.GetInt32(1);

        if (total != 14 || sentinel != 1)
        {
            throw new InvalidOperationException(
                $"La base apuntada por {VariableName} NO parece la base de prueba (se esperaban los 14 artículos de " +
                "sql/dev/02-datos-prueba.sql). Por seguridad no se ejecuta ninguna prueba de integración. " +
                "Nunca apuntes esta variable a la base real de Aspel.");
        }
    }
}

/// <summary>[Fact] que se omite sola cuando no hay base de prueba configurada.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!IntegrationEnvironment.IsConfigured) Skip = IntegrationEnvironment.SkipReason;
    }
}

/// <summary>[Theory] que se omite sola cuando no hay base de prueba configurada.</summary>
public sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!IntegrationEnvironment.IsConfigured) Skip = IntegrationEnvironment.SkipReason;
    }
}
