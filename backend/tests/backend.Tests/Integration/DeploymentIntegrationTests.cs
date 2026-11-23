using System.Net;
using backend.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace backend.Tests.Integration;

/// <summary>
/// Pruebas de integración de lo que necesita el despliegue en Railway (#47): migrate, /api/health y las llaves
/// de Data Protection en PostgreSQL.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeploymentIntegrationTests
{
    [PostgresFact]
    public async Task Migrate_aplica_todo_en_una_base_vacia_y_despues_no_deja_pendientes()
    {
        await using var database = await EmptyDatabase.CreateAsync();
        await using var factory = CreateFactory(database.ConnectionString);

        Assert.Equal(0, await MigrateCommand.RunAsync(factory.Services, TestContext.Current.CancellationToken));
        // Segunda vez: no hay nada que hacer y no falla.
        Assert.Equal(0, await MigrateCommand.RunAsync(factory.Services, TestContext.Current.CancellationToken));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [PostgresFact]
    public async Task Health_responde_200_con_postgresql()
    {
        await using var database = await TempDatabase.CreateAsync();
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/api/health", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(ct));
    }

    [PostgresFact]
    public async Task Las_llaves_de_data_protection_sobreviven_a_un_host_nuevo()
    {
        await using var database = await TempDatabase.CreateAsync();
        var ct = TestContext.Current.CancellationToken;

        // Primer host (equivale al contenedor del despliegue anterior): protege un texto.
        string protectedText;
        List<int> keysAfterFirstHost;
        await using (var first = CreateFactory(database.ConnectionString))
        {
            protectedText = first.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(DataProtectionCheckCommand.Purpose)
                .Protect("hola-nettes");

            keysAfterFirstHost = await KeyIdsAsync(database, ct);
        }

        Assert.NotEmpty(keysAfterFirstHost);

        // Segundo host, sin nada en memoria del primero (equivale a un contenedor nuevo contra la misma base).
        await using var second = CreateFactory(database.ConnectionString);
        var unprotected = second.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(DataProtectionCheckCommand.Purpose)
            .Unprotect(protectedText);

        Assert.Equal("hola-nettes", unprotected);
        Assert.Equal(keysAfterFirstHost, await KeyIdsAsync(database, ct));
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString));

    private static async Task<List<int>> KeyIdsAsync(TempDatabase database, CancellationToken ct)
    {
        await using var context = database.CreateContext();
        return await context.DataProtectionKeys.OrderBy(k => k.Id).Select(k => k.Id).ToListAsync(ct);
    }

    /// <summary>Base temporal SIN migraciones, para probar el comando migrate desde cero.</summary>
    private sealed class EmptyDatabase : IAsyncDisposable
    {
        private readonly string _admin;
        private readonly string _name;

        private EmptyDatabase(string admin, string name)
        {
            _admin = admin;
            _name = name;
            ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
        }

        public string ConnectionString { get; }

        public static async Task<EmptyDatabase> CreateAsync()
        {
            var admin = IntegrationEnvironment.ConnectionString!;
            var name = "deploy_test_" + Guid.NewGuid().ToString("N");

            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();

            return new EmptyDatabase(admin, name);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();

            await using var connection = new NpgsqlConnection(_admin);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
    }
}
