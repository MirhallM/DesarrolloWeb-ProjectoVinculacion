using Microsoft.Extensions.Logging.Abstractions;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Publishing;
using SyncAgent.Resilience;
using SyncAgent.State;

namespace SyncAgent.Tests.Integration;

/// <summary>
/// Lee la base de PRUEBA de Docker con el lector real. Los valores esperados están en el encabezado de
/// sync-agent/sql/dev/02-datos-prueba.sql.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AspelReaderIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "syncagent-int-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static AspelProductReader Reader(int priceList = 1) =>
        new(IntegrationEnvironment.ConnectionString!, new AspelOptions { PriceList = priceList, ConnectTimeoutSeconds = 10 });

    private static async Task<List<AspelProduct>> ReadAllAsync(int priceList = 1, AspelReadMetrics? metrics = null)
    {
        await IntegrationEnvironment.EnsureTestDatabaseAsync();

        var products = new List<AspelProduct>();
        await foreach (var product in Reader(priceList).ReadAsync(metrics, TestContext.Current.CancellationToken))
        {
            products.Add(product);
        }

        return products;
    }

    [IntegrationFact]
    public async Task Lee_los_14_articulos_de_prueba_en_streaming_sin_duplicar()
    {
        var products = await ReadAllAsync();

        Assert.Equal(14, products.Count);
        Assert.Equal(14, products.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [IntegrationFact]
    public async Task Los_activos_sin_precio_en_la_lista_1_no_se_pierden_y_llegan_con_precio_nulo()
    {
        var products = await ReadAllAsync();

        var onlyList4 = Assert.Single(products, p => p.Code == "7501000100035"); // solo tiene precio en la lista 4
        var noPriceAtAll = Assert.Single(products, p => p.Code == "7501000100127"); // no tiene ninguna fila de precio
        Assert.Equal("A", onlyList4.Status);
        Assert.Null(onlyList4.Price);
        Assert.Null(noPriceAtAll.Price);

        var withoutPrice = products.Where(p => p.Status == "A" && p.Price is null).ToList();
        Assert.Equal(2, withoutPrice.Count);
    }

    [IntegrationFact]
    public async Task Los_codigos_con_y_sin_espacios_al_inicio_son_dos_productos_distintos_y_llegan_intactos()
    {
        var products = await ReadAllAsync();

        var plain = Assert.Single(products, p => p.Code == "2050000117121");
        var padded = Assert.Single(products, p => p.Code == "  2050000117121");
        Assert.Equal("PAN BLANCO BOLSA", plain.Description);
        Assert.Equal("PAN INTEGRAL BOLSA", padded.Description);
        Assert.Equal(40, plain.Price);
        Assert.Equal(55, padded.Price);
    }

    [IntegrationFact]
    public async Task Los_valores_llegan_exactos_sin_redondeo_ni_recorte()
    {
        var products = await ReadAllAsync();
        AspelProduct Get(string code) => Assert.Single(products, p => p.Code == code);

        Assert.Equal(34.999999, Get("7501000100103").Price);   // precio con decimales de más: la API lo redondea, no el agente
        Assert.Equal(0, Get("7501000100110").Price);
        Assert.Equal(-3, Get("7501000100080").Stock);          // existencia negativa
        Assert.Equal(2.75, Get("7501000100028").Stock);        // con decimales
        Assert.Equal(500000000000, Get("7501000100097").Stock); // inflada

        Assert.Equal("", Get("7501000100059").Description);     // vacía
        Assert.Equal("NULL", Get("7501000100066").Description); // el texto literal
        Assert.Null(Get("7501000100073").Description);          // NULL de la base
        Assert.Equal("B", Get("7501000100042").Status);
    }

    [IntegrationFact]
    public async Task La_lista_de_precios_es_un_parametro_y_cambia_los_precios_leidos()
    {
        var list4 = await ReadAllAsync(priceList: 4);

        Assert.Equal(14, list4.Count); // sigue habiendo una fila por artículo
        Assert.Equal(18, Assert.Single(list4, p => p.Code == "7501000100035").Price);
        Assert.Equal(30, Assert.Single(list4, p => p.Code == "7501000100011").Price);
        Assert.Equal(2, list4.Count(p => p.Price is not null)); // solo esos dos artículos tienen precio en la lista 4
    }

    [IntegrationFact]
    public async Task Mide_el_tiempo_de_conexion_y_de_consulta()
    {
        var metrics = new AspelReadMetrics();

        await ReadAllAsync(metrics: metrics);

        Assert.True(metrics.ConnectDuration > TimeSpan.Zero);
        Assert.True(metrics.QueryDuration > TimeSpan.Zero);
    }

    [IntegrationFact]
    public async Task El_resumen_coincide_con_los_casos_sembrados()
    {
        var s = CatalogSummaryBuilder.From(await ReadAllAsync());

        Assert.Equal(14, s.TotalRows);
        Assert.Equal(14, s.DistinctCodes);
        Assert.Equal(13, s.Active);
        Assert.Equal(1, s.Inactive);
        Assert.Equal(0, s.OtherStatus);
        Assert.Equal(2, s.ActiveWithoutPrice);
        Assert.Equal(1, s.ActiveWithZeroPrice);
        Assert.Equal(1, s.DescriptionNull);
        Assert.Equal(1, s.DescriptionEmpty);
        Assert.Equal(1, s.DescriptionLiteralNull);
        Assert.Equal(3, s.ActiveWithoutUsableDescription);
        Assert.Equal(1, s.CodesWithLeadingSpaces);
        Assert.Equal(1, s.CodeGroupsDifferingOnlyBySpaces);
        Assert.Equal(1, s.StockNegative);
        Assert.Equal(1, s.StockWithDecimals);
        Assert.Equal(1, s.StockOver10Thousand);
        Assert.Equal(1, s.StockOver1Million);
        Assert.Equal(15, s.MaxCodeLength);
    }

    [IntegrationFact]
    public async Task Un_ciclo_completo_lee_la_base_y_deja_el_estado_en_disco()
    {
        await IntegrationEnvironment.EnsureTestDatabaseAsync();

        var options = new AspelOptions { ConnectTimeoutSeconds = 10 };
        var redactor = SecretRedactor.FromConnectionString(IntegrationEnvironment.ConnectionString);
        var loader = new AspelProductLoader(
            Reader(), new RetryPolicy(options.Retry), new SqlErrorDescriber(redactor), NullLogger<AspelProductLoader>.Instance);
        var store = new SyncStateStore(_dir, NullLogger<SyncStateStore>.Instance);
        var cycle = new SyncCycle(loader, new NoOpCatalogPublisher(NullLogger<NoOpCatalogPublisher>.Instance), store,
            redactor, TimeProvider.System, NullLogger<SyncCycle>.Instance);

        var exit = await cycle.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        var state = store.Load();
        Assert.Equal(SyncResults.Succeeded, state.LastResult);
        Assert.Equal(14, state.ProductsRead);
        Assert.NotNull(state.LastSuccessAt);
        Assert.Equal(0, state.ConsecutiveFailures);
    }
}
