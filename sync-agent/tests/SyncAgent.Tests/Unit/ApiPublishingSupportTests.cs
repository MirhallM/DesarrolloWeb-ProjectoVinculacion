using Microsoft.Extensions.Configuration;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Publishing;

namespace SyncAgent.Tests.Unit;

/// <summary>Huella del inventario, opciones de la sección Api y la comprobación de la API en <c>--test</c>.</summary>
public sealed class ApiPublishingSupportTests
{
    private const string Key = "llave-de-ejemplo-no-usar-0000000000000000000";

    // ---------- InventoryHasher ----------

    private static AspelProduct P(string code, string? description = "X", double? stock = 1, string? status = "A", double? price = 10) =>
        new(code, description, stock, status, price);

    [Fact]
    public void La_huella_es_determinista_e_independiente_del_orden()
    {
        var a = new[] { P("A"), P("B"), P("  B") };
        var b = new[] { P("  B"), P("A"), P("B") };

        Assert.Equal(InventoryHasher.Compute(a), InventoryHasher.Compute(b));
        Assert.Matches("^[0-9a-f]{64}$", InventoryHasher.Compute(a));
    }

    [Theory]
    [InlineData("  A", "X", 1.0, "A", 10.0)]   // código con espacios: otro artículo
    [InlineData("A", "Y", 1.0, "A", 10.0)]     // descripción
    [InlineData("A", "X", 1.5, "A", 10.0)]     // existencia
    [InlineData("A", "X", 1.0, "B", 10.0)]     // estado
    [InlineData("A", "X", 1.0, "A", 10.000001)] // precio con decimales de más
    public void Cualquier_cambio_cambia_la_huella(string code, string description, double stock, string status, double price)
    {
        var original = InventoryHasher.Compute([P("A")]);
        Assert.NotEqual(original, InventoryHasher.Compute([P(code, description, stock, status, price)]));
    }

    [Fact]
    public void Un_nulo_no_se_confunde_con_un_texto()
    {
        Assert.NotEqual(InventoryHasher.Compute([P("A", description: null)]), InventoryHasher.Compute([P("A", description: "N")]));
        Assert.NotEqual(InventoryHasher.Compute([P("A", price: null)]), InventoryHasher.Compute([P("A", price: 0)]));
        Assert.NotEqual(InventoryHasher.Compute([]), InventoryHasher.Compute([P("A")]));
    }

    // ---------- ApiOptions ----------

    private static ApiOptions Load(params (string Key, string? Value)[] values) =>
        ApiOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(
            values.Select(v => new KeyValuePair<string, string?>($"Api:{v.Key}", v.Value))).Build());

    [Fact]
    public void Opciones_validas_con_https_y_valores_de_fabrica()
    {
        var options = Load(("BaseUrl", "https://nettes.up.railway.app"), ("AgentKey", Key));

        Assert.True(options.IsConfigured);
        Assert.Empty(options.Validate());
        Assert.Equal(1000, options.BatchSize);
        Assert.Equal(4, options.Retry.MaxAttempts);
        Assert.Equal([5, 15, 45], options.Retry.DelaysSeconds);
        Assert.Equal("https://nettes.up.railway.app/", options.BaseUri.ToString());
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://localhost")]
    [InlineData("HTTP://LOCALHOST:3000")]
    [InlineData("http://127.0.0.1:5039/")]
    [InlineData("http://[::1]:3000")]
    public void Http_solo_se_admite_en_este_equipo(string url) =>
        Assert.Empty(Load(("BaseUrl", url), ("AgentKey", Key)).Validate());

    [Theory]
    [InlineData("http://nettes.up.railway.app", "https")]
    [InlineData("http://localhost.example.test", "https")]        // empieza con "localhost" pero no es loopback
    [InlineData("http://127.0.0.1.example.test", "https")]
    [InlineData("http://10.1.2.3:3000", "https")]                  // red local: tampoco
    [InlineData("http://0.0.0.0:3000", "https")]
    [InlineData("http://user@localhost:3000", "URL válida")]
    [InlineData("ftp://nettes.example.test", "URL válida")]
    [InlineData("nettes.up.railway.app", "URL válida")]
    [InlineData("https://<dominio>.up.railway.app", "URL válida")]
    [InlineData("https://nettes.example.test/?x=1", "URL válida")]
    public void Una_url_insegura_o_invalida_se_rechaza(string url, string expected)
    {
        var errors = Load(("BaseUrl", url), ("AgentKey", Key)).Validate();
        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CAMBIAR")]
    [InlineData("corta")]
    [InlineData("llave con espacios no sirve 00000000000000000")]
    public void Una_llave_ausente_o_invalida_se_rechaza_sin_repetirla(string? key)
    {
        var options = Load(("BaseUrl", "https://nettes.up.railway.app"), ("AgentKey", key));
        var errors = options.Validate();

        Assert.Contains(errors, e => e.Contains("Api:AgentKey"));
        if (!string.IsNullOrEmpty(key))
        {
            Assert.All(errors, e => Assert.DoesNotContain(key, e));
        }
    }

    [Fact]
    public void El_ejemplo_de_la_tienda_cuenta_como_sin_configurar()
    {
        Assert.False(Load(("BaseUrl", "https://<dominio>.up.railway.app"), ("AgentKey", "CAMBIAR")).IsConfigured);
        Assert.False(Load().IsConfigured);
    }

    [Theory]
    [InlineData("BatchSize", "99")]
    [InlineData("BatchSize", "2001")]
    [InlineData("MaxAttempts", "0")]
    [InlineData("TotalTimeoutMinutes", "0")]
    public void Valores_fuera_de_rango_se_rechazan(string name, string value) =>
        Assert.NotEmpty(Load(("BaseUrl", "https://nettes.up.railway.app"), ("AgentKey", Key), (name, value)).Validate());

    [Fact]
    public void Un_numero_mal_escrito_no_repite_el_valor()
    {
        var options = Load(("BaseUrl", "https://nettes.up.railway.app"), ("AgentKey", Key), ("BatchSize", "mil"));

        var error = Assert.Single(options.Validate());
        Assert.DoesNotContain("mil", error);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void El_redactor_oculta_la_llave_ademas_de_la_cadena_de_conexion()
    {
        var redactor = SecretRedactor.FromConnectionString("Server=x;Database=y;User Id=u;Password=Cl4ve-Secreta;").With(Key, null, "");

        var text = redactor.Redact($"llave {Key} y clave Cl4ve-Secreta");

        Assert.DoesNotContain(Key, text);
        Assert.DoesNotContain("Cl4ve-Secreta", text);
    }

    // ---------- --test ----------

    private sealed class StubCheck(Exception? failure = null) : IApiConnectivityCheck
    {
        public int Calls { get; private set; }

        public Task PingAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class Reader : IAspelProductReader
    {
        public async IAsyncEnumerable<AspelProduct> ReadAsync(AspelReadMetrics? metrics = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return P("A");
        }
    }

    private static async Task<(int Exit, string Output)> RunTestMode(IApiConnectivityCheck? check)
    {
        var redactor = SecretRedactor.None;
        var loader = new AspelProductLoader(new Reader(),
            new SyncAgent.Resilience.RetryPolicy(new SyncAgent.Resilience.RetryOptions { MaxAttempts = 1 }),
            new SqlErrorDescriber(redactor), Microsoft.Extensions.Logging.Abstractions.NullLogger<AspelProductLoader>.Instance);
        using var output = new StringWriter();
        var runner = new TestModeRunner(loader, new AspelOptions(), "servidor de prueba", output, check, "https://nettes.example.test");
        var exit = await runner.RunAsync(TestContext.Current.CancellationToken);
        return (exit, output.ToString());
    }

    [Fact]
    public async Task Test_comprueba_la_api_y_la_llave()
    {
        var check = new StubCheck();
        var (exit, output) = await RunTestMode(check);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(1, check.Calls);
        Assert.Contains("la llave es válida", output);
    }

    [Fact]
    public async Task Test_con_la_llave_rechazada_sale_con_5()
    {
        var (exit, output) = await RunTestMode(new StubCheck(
            new ApiPublishException("La API rechazó la llave del agente (401).", isTransient: false, 401)));

        Assert.Equal(ExitCodes.ApiFailure, exit);
        Assert.Contains("401", output);
    }

    [Fact]
    public async Task Test_sin_red_sale_con_5()
    {
        var (exit, output) = await RunTestMode(new StubCheck(
            new ApiPublishException("No se pudo conectar con la API (no se pudo abrir la conexión).", isTransient: true)));

        Assert.Equal(ExitCodes.ApiFailure, exit);
        Assert.Contains("NO SE PUDO COMPROBAR", output);
    }

    [Fact]
    public async Task Test_sin_api_configurada_solo_avisa()
    {
        var (exit, output) = await RunTestMode(null);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Sin configurar", output);
    }
}
