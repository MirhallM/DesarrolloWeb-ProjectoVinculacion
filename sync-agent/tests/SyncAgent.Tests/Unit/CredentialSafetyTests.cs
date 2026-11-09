using Microsoft.Data.SqlClient;
using SyncAgent.Diagnostics;

namespace SyncAgent.Tests.Unit;

public class CredentialSafetyTests
{
    private const string User = "catalogo_lectura";
    private const string Password = "Cl4ve-Sup3r-Secreta!";

    private static string ConnectionString(string server = "localhost") =>
        $"Server={server};Database=SAE70Empre01;User Id={User};Password={Password};TrustServerCertificate=True;";

    // ---------- ConnectionStringDescriber ----------

    [Fact]
    public void Describe_muestra_servidor_y_base_pero_nunca_usuario_ni_clave()
    {
        var text = ConnectionStringDescriber.Describe(ConnectionString());

        Assert.Contains("localhost", text);
        Assert.Contains("SAE70Empre01", text);
        Assert.DoesNotContain(User, text);
        Assert.DoesNotContain(Password, text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Describe_sin_cadena_dice_que_no_esta_configurada(string? value)
    {
        Assert.Contains("sin configurar", ConnectionStringDescriber.Describe(value));
    }

    [Fact]
    public void Describe_con_cadena_mal_formada_no_repite_la_cadena()
    {
        var broken = $"esto no es una cadena; Password={Password}; {{{{";

        var text = ConnectionStringDescriber.Describe(broken);

        Assert.DoesNotContain(Password, text);
        Assert.Contains("inválido", text);
    }

    // ---------- SecretRedactor ----------

    [Fact]
    public void Redactor_oculta_usuario_clave_y_cadena_completa()
    {
        var connectionString = ConnectionString();
        var redactor = SecretRedactor.FromConnectionString(connectionString);

        var text = redactor.Redact($"fallo con {connectionString}; usuario {User}; clave {Password}");

        Assert.DoesNotContain(User, text);
        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain("SAE70Empre01", text); // la cadena completa se sustituye
    }

    [Fact]
    public void Redactor_oculta_el_usuario_del_mensaje_de_login_fallido_aunque_no_sea_el_configurado()
    {
        var text = SecretRedactor.None.Redact("Login failed for user 'otro_usuario'.");

        Assert.DoesNotContain("otro_usuario", text);
        Assert.Contains(SecretRedactor.Mask, text);
    }

    [Fact]
    public void Redactor_con_cadena_invalida_no_falla_y_oculta_la_cadena_entera()
    {
        var broken = "basura;;; Password=abc123456";
        var redactor = SecretRedactor.FromConnectionString(broken);

        Assert.DoesNotContain("abc123456", redactor.Redact($"error: {broken}"));
    }

    // ---------- SqlErrorDescriber ----------

    [Fact]
    public void Describer_oculta_el_usuario_en_el_mensaje_de_login_fallido()
    {
        var describer = new SqlErrorDescriber(SecretRedactor.FromConnectionString(ConnectionString()));
        var ex = new InvalidOperationException($"Login failed for user '{User}'. Password: {Password}");

        var text = describer.Describe(ex);

        Assert.DoesNotContain(User, text);
        Assert.DoesNotContain(Password, text);
    }

    [Theory]
    [InlineData(18456)]
    [InlineData(4060)]
    [InlineData(229)]
    [InlineData(208)]
    [InlineData(-2)]
    [InlineData(53)]
    [InlineData(258)]
    public void Describer_tiene_explicacion_en_espanol_para_los_errores_comunes(int number)
    {
        Assert.DoesNotContain("no catalogado", SqlErrorDescriber.Explain(number));
    }

    // ---------- Excepción REAL de SqlClient ----------

    /// <summary>
    /// Provoca una SqlException de verdad (puerto 1 cerrado, así que no necesita base de datos) y comprueba que
    /// ni el texto crudo de la excepción ni nuestra descripción contienen la contraseña ni la cadena de conexión.
    /// </summary>
    [Fact]
    public async Task Una_SqlException_real_no_filtra_la_cadena_de_conexion()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString("127.0.0.1,1"))
        {
            ConnectTimeout = 3,
            Pooling = false,
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        var ex = await Assert.ThrowsAnyAsync<SqlException>(
            () => connection.OpenAsync(TestContext.Current.CancellationToken));

        // 1) Lo que produce SqlClient por sí solo no trae la contraseña ni la cadena.
        var raw = ex.ToString();
        Assert.DoesNotContain(Password, raw);
        Assert.DoesNotContain(builder.ConnectionString, raw);

        // 2) Y lo que registramos nosotros tampoco.
        var describer = new SqlErrorDescriber(SecretRedactor.FromConnectionString(builder.ConnectionString));
        var safe = describer.Describe(ex);
        Assert.DoesNotContain(Password, safe);
        Assert.DoesNotContain(User, safe);
        Assert.DoesNotContain(builder.ConnectionString, safe);
        Assert.Contains("Error de SQL Server", safe);
    }
}
