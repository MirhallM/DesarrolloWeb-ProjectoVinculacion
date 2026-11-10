using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Resilience;

namespace SyncAgent.Tests.Integration;

/// <summary>
/// Seguridad contra la base de PRUEBA: la cuenta es de solo lectura y un error de conexión no filtra credenciales.
/// Antes de cualquier prueba se comprueba que la base sea la de prueba (ver IntegrationEnvironment).
/// </summary>
[Trait("Category", "Integration")]
public class AspelSecurityIntegrationTests
{
    private const string WrongPassword = "Contrasena-Incorrecta-9!";

    private static SqlConnectionStringBuilder Builder() => new(IntegrationEnvironment.ConnectionString) { Pooling = false, ConnectTimeout = 10 };

    // ---------- La cuenta NO puede escribir ----------

    [IntegrationTheory]
    [InlineData("INSERT INTO dbo.INVE01 (CVE_ART, DESCR, EXIST, STATUS) VALUES ('ZZ-NO-DEBE-EXISTIR', 'X', 0, 'A')")]
    [InlineData("UPDATE dbo.INVE01 SET DESCR = 'MODIFICADO'")]
    [InlineData("DELETE FROM dbo.INVE01")]
    [InlineData("INSERT INTO dbo.PRECIO_X_PROD01 (CVE_ART, CVE_PRECIO, PRECIO) VALUES ('ZZ-NO-DEBE-EXISTIR', 1, 1)")]
    [InlineData("UPDATE dbo.PRECIO_X_PROD01 SET PRECIO = 0")]
    [InlineData("DELETE FROM dbo.PRECIO_X_PROD01")]
    public async Task Escribir_con_la_cuenta_de_lectura_falla_con_permiso_denegado(string writeStatement)
    {
        await IntegrationEnvironment.EnsureTestDatabaseAsync();

        await using var connection = new SqlConnection(Builder().ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Por si la cuenta tuviera permiso de escritura por error: dentro de una transacción que SIEMPRE se revierte.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = writeStatement;

            var ex = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

            Assert.Equal(229, ex.Number); // The ... permission was denied on the object
        }
        finally
        {
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
    }

    [IntegrationFact]
    public async Task La_cuenta_no_pertenece_a_ningun_rol_de_la_base_ni_es_sysadmin()
    {
        await IntegrationEnvironment.EnsureTestDatabaseAsync();

        await using var connection = new SqlConnection(Builder().ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT IS_ROLEMEMBER('db_datareader'), IS_ROLEMEMBER('db_datawriter'), " +
            "       IS_ROLEMEMBER('db_ddladmin'), IS_ROLEMEMBER('db_owner'), IS_SRVROLEMEMBER('sysadmin')";

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(0, reader.GetInt32(i));
        }
    }

    // ---------- Un error de conexión no filtra credenciales ----------

    [IntegrationFact]
    public async Task Con_contrasena_incorrecta_la_excepcion_de_SqlClient_y_nuestro_texto_no_filtran_la_clave()
    {
        await IntegrationEnvironment.EnsureTestDatabaseAsync();

        var builder = Builder();
        var user = builder.UserID;
        builder.Password = WrongPassword;

        await using var connection = new SqlConnection(builder.ConnectionString);
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.OpenAsync(TestContext.Current.CancellationToken));

        Assert.Equal(18456, ex.Number);

        // 1) La excepción cruda de SqlClient: no trae la contraseña ni la cadena de conexión...
        var raw = ex.ToString();
        Assert.DoesNotContain(WrongPassword, raw);
        Assert.DoesNotContain(builder.ConnectionString, raw);
        // ... pero SÍ trae el nombre de usuario ("Login failed for user '...'"), por eso nunca se registra tal cual.
        Assert.Contains(user, raw);

        // 2) Lo que registra el agente no trae ni usuario ni contraseña.
        var describer = new SqlErrorDescriber(SecretRedactor.FromConnectionString(builder.ConnectionString));
        var safe = describer.Describe(ex);
        Assert.DoesNotContain(WrongPassword, safe);
        Assert.DoesNotContain(user, safe);
        Assert.Contains("18456", safe);
    }

    [IntegrationFact]
    public async Task El_cargador_con_contrasena_incorrecta_no_reintenta_y_lanza_un_error_sin_credenciales()
    {
        await IntegrationEnvironment.EnsureTestDatabaseAsync();

        var builder = Builder();
        var user = builder.UserID;
        builder.Password = WrongPassword;
        var options = new AspelOptions { ConnectTimeoutSeconds = 10 };

        var waits = new List<TimeSpan>();
        var loader = new AspelProductLoader(
            new AspelProductReader(builder.ConnectionString, options),
            new RetryPolicy(new AspelOptions.RetryOptions { MaxAttempts = 4, DelaysSeconds = [1] },
                (wait, _) => { waits.Add(wait); return Task.CompletedTask; }),
            new SqlErrorDescriber(SecretRedactor.FromConnectionString(builder.ConnectionString)),
            NullLogger<AspelProductLoader>.Instance);

        var ex = await Assert.ThrowsAsync<AspelReadException>(() => loader.LoadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(18456, ex.SqlErrorNumber);
        Assert.Empty(waits);                      // un login fallido no se reintenta: solo bloquearía la cuenta
        Assert.Null(ex.InnerException);           // no se conserva la excepción original (trae el usuario)
        Assert.DoesNotContain(WrongPassword, ex.ToString());
        Assert.DoesNotContain(user, ex.ToString());
    }
}
