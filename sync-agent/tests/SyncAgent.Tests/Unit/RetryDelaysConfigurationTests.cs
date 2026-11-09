using Microsoft.Extensions.Configuration;
using SyncAgent.Aspel;

namespace SyncAgent.Tests.Unit;

/// <summary>
/// La lista de esperas que trae <c>appsettings.Production.json</c> debe REEMPLAZAR por completo a la de fábrica,
/// tenga los valores que tenga. Se prueba con archivos JSON reales (el appsettings.json que se distribuye más un
/// Production escrito en una carpeta temporal) porque .NET apila las fuentes por posición y un diccionario en
/// memoria no reproduce ese comportamiento.
/// </summary>
public sealed class RetryDelaysConfigurationTests : IDisposable
{
    private const string DelaysKey = "Aspel:Retry:DelaysSeconds";

    private static readonly string ShippedAppSettings =
        Path.Combine(AppContext.BaseDirectory, "shipped", "appsettings.json");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "syncagent-cfg-" + Guid.NewGuid().ToString("N"));

    public RetryDelaysConfigurationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Configuración como en la tienda: appsettings.json real + (opcional) appsettings.Production.json.</summary>
    private AspelOptions LoadWithProduction(string? productionJson)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(ShippedAppSettings, optional: false);
        if (productionJson is not null)
        {
            var path = Path.Combine(_dir, "appsettings.Production.json");
            File.WriteAllText(path, productionJson);
            builder.AddJsonFile(path, optional: false);
        }

        return AspelOptions.Load(builder.Build());
    }

    private static string Delays(string jsonArray) => $$"""{ "Aspel": { "Retry": { "DelaysSeconds": {{jsonArray}} } } }""";

    // ---------- La lista de Production reemplaza por completo a la de fábrica ----------

    [Fact]
    public void Una_lista_de_2_valores_reemplaza_a_la_de_fabrica_sin_dejar_el_tercero()
    {
        var options = LoadWithProduction(Delays("[1, 2]"));

        Assert.Equal([1, 2], options.Retry.DelaysSeconds); // antes daba [1, 2, 45]
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Una_lista_de_1_valor_reemplaza_a_la_de_fabrica()
    {
        Assert.Equal([7], LoadWithProduction(Delays("[7]")).Retry.DelaysSeconds);
    }

    [Fact]
    public void Una_lista_de_4_valores_reemplaza_a_la_de_fabrica_y_no_se_agrega_a_ella()
    {
        Assert.Equal([1, 2, 3, 4], LoadWithProduction(Delays("[1, 2, 3, 4]")).Retry.DelaysSeconds);
    }

    [Fact]
    public void Sin_la_clave_en_Production_se_usan_los_valores_de_fabrica_del_codigo()
    {
        var options = LoadWithProduction("""{ "Aspel": { "PriceList": 4 } }""");

        Assert.Equal([5, 15, 45], options.Retry.DelaysSeconds);
        Assert.Equal(4, options.PriceList); // el resto de la sección se sigue leyendo
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Sin_archivo_Production_se_usan_los_valores_de_fabrica()
    {
        Assert.Equal([5, 15, 45], LoadWithProduction(null).Retry.DelaysSeconds);
    }

    // ---------- Listas inválidas: el agente no debe arrancar y debe decir por qué ----------

    [Fact]
    public void Una_lista_vacia_es_un_error_y_no_cae_en_silencio_a_los_valores_de_fabrica()
    {
        var options = LoadWithProduction(Delays("[]"));

        var errors = options.Validate();
        Assert.Single(errors);
        Assert.Contains("DelaysSeconds", errors[0]);
        Assert.Contains("vacía", errors[0]);
    }

    [Fact]
    public void Valores_negativos_son_un_error_que_indica_cuales()
    {
        var options = LoadWithProduction(Delays("[-1, 5, -30]"));

        var errors = options.Validate();
        Assert.Single(errors);
        Assert.Contains("negativos", errors[0]);
        Assert.Contains("-1", errors[0]);
        Assert.Contains("-30", errors[0]);
    }

    [Fact]
    public void Un_texto_donde_va_un_numero_es_un_error_sin_repetir_el_texto()
    {
        var options = LoadWithProduction(Delays("""["abc", 5]"""));

        var errors = options.Validate();
        Assert.NotEmpty(errors);
        Assert.DoesNotContain(errors, e => e.Contains("abc"));
    }

    [Fact]
    public void Un_valor_suelto_en_vez_de_una_lista_es_un_error()
    {
        // Equivale a definir la variable de entorno Aspel__Retry__DelaysSeconds=5.
        var options = AspelOptions.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DelaysKey] = "5" })
            .Build());

        Assert.NotEmpty(options.Validate());
    }

    [Fact]
    public void Una_lista_con_huecos_en_las_posiciones_es_un_error()
    {
        var options = AspelOptions.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{DelaysKey}:0"] = "1", [$"{DelaysKey}:2"] = "3" })
            .Build());

        Assert.Contains(options.Validate(), e => e.Contains("huecos"));
    }

    // ---------- Guarda contra reintroducir la causa de la mezcla ----------

    [Fact]
    public void El_appsettings_json_distribuido_no_define_las_esperas()
    {
        // Si alguien vuelve a poner DelaysSeconds en appsettings.json, una lista más corta en Production
        // se mezclaría por posición con ella ([a, b, 45]). Los valores de fábrica viven solo en el código.
        var text = File.ReadAllText(ShippedAppSettings);

        Assert.DoesNotContain("DelaysSeconds", text);
    }

    [Fact]
    public void Los_valores_de_fabrica_estan_en_el_codigo()
    {
        Assert.Equal([5, 15, 45], new AspelOptions().Retry.DelaysSeconds);
    }
}
