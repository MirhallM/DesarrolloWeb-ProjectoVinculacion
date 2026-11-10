using System.Runtime.CompilerServices;
using backend.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend.Tests.Integration;

/// <summary>
/// Datos para las pruebas que usan PostgreSQL (docker compose up -d). Sin la variable BACKEND_TEST_CONNECTION
/// esas pruebas se omiten, así que el CI (que además las excluye con Category=Integration) y quien no tenga
/// PostgreSQL pueden correr el resto sin problema.
/// </summary>
/// <remarks>
/// La cadena apunta al SERVIDOR; hace falta un usuario que pueda crear bases y extensiones (el de docker-compose sirve).
/// Cada prueba crea su propia base temporal y la borra al terminar: nunca toca la base de desarrollo.
/// </remarks>
public static class IntegrationEnvironment
{
    public const string ConnectionVariable = "BACKEND_TEST_CONNECTION";

    public const string InventoryVariable = "CATALOG_INVENTORY_CSV";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionVariable);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public static string SkipReason =>
        $"Define la variable de entorno {ConnectionVariable} con la cadena de un PostgreSQL de pruebas (ver backend/README o docker-compose.yml).";
}

/// <summary>[Fact] que se omite sola cuando no hay PostgreSQL de prueba configurado.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!IntegrationEnvironment.IsConfigured)
        {
            Skip = IntegrationEnvironment.SkipReason;
        }
    }
}

/// <summary>Base de datos temporal con las migraciones aplicadas; se borra al liberarla.</summary>
public sealed class TempDatabase : IAsyncDisposable
{
    private readonly string _adminConnection;
    private readonly string _name;

    private TempDatabase(string adminConnection, string name, string connectionString)
    {
        _adminConnection = adminConnection;
        _name = name;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<TempDatabase> CreateAsync()
    {
        var admin = IntegrationEnvironment.ConnectionString!;
        var name = "catalog_seed_test_" + Guid.NewGuid().ToString("N");

        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        var database = new TempDatabase(admin, name, new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString);

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
