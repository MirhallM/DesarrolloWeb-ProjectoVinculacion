using SyncAgent.Aspel;
using SyncAgent.Diagnostics;

namespace SyncAgent.Tests.Unit;

public class CatalogSummaryTests
{
    private static AspelProduct P(string code, string? descr = "ALGO", double? stock = 1, string? status = "A", double? price = 10) =>
        new(code, descr, stock, status, price);

    [Fact]
    public void Cuenta_activos_bajas_y_otros_estados()
    {
        var s = CatalogSummaryBuilder.From([
            P("1"),
            P("2"),
            P("3", status: "B"),
            P("4", status: "X"),
            P("5", status: null),
        ]);

        Assert.Equal(5, s.TotalRows);
        Assert.Equal(2, s.Active);
        Assert.Equal(1, s.Inactive);
        Assert.Equal(2, s.OtherStatus);
        Assert.Equal(["X", "(NULL)"], s.OtherStatusSamples);
    }

    [Fact]
    public void Precios_problematicos_solo_cuentan_en_los_activos()
    {
        var s = CatalogSummaryBuilder.From([
            P("1", price: null),               // activo sin precio
            P("2", price: null, status: "B"),  // de baja: no cuenta
            P("3", price: 0),                  // activo con precio 0
            P("4", price: 250_000_000),        // activo con precio sospechoso
            P("5", price: 25.5),               // normal
        ]);

        Assert.Equal(1, s.ActiveWithoutPrice);
        Assert.Equal(1, s.ActiveWithZeroPrice);
        Assert.Equal(1, s.ActiveWithSuspiciousPrice);
    }

    [Fact]
    public void Distingue_descripcion_nula_vacia_y_literal_NULL()
    {
        var s = CatalogSummaryBuilder.From([
            P("1", descr: null),                 // nula        (activo)
            P("2", descr: ""),                   // vacía       (activo)
            P("3", descr: "   "),                // solo espacios (activo)
            P("4", descr: "NULL"),               // literal     (activo)
            P("5", descr: "null "),              // literal con otra capitalización y espacio (activo)
            P("6", descr: "LECHE"),              // normal
            P("7", descr: "", status: "B"),      // vacía pero de baja
        ]);

        Assert.Equal(1, s.DescriptionNull);
        Assert.Equal(3, s.DescriptionEmpty);          // "", "   " y la de baja
        Assert.Equal(2, s.DescriptionLiteralNull);    // "NULL" y "null "
        Assert.Equal(5, s.ActiveWithoutUsableDescription); // los 5 activos problemáticos; la de baja no cuenta
    }

    [Fact]
    public void Detecta_codigos_que_solo_difieren_por_espacios_sin_modificarlos()
    {
        var s = CatalogSummaryBuilder.From([
            P("2050000117121"),
            P("  2050000117121"),
            P("ABC"),
            P("ABC "),
            P(" ABC"),
            P("XYZ"),
        ]);

        Assert.Equal(6, s.TotalRows);
        Assert.Equal(6, s.DistinctCodes);                   // los 6 son códigos distintos
        Assert.Equal(2, s.CodeGroupsDifferingOnlyBySpaces); // el grupo "2050000117121" (2) y el grupo "ABC" (3)
        Assert.Equal(2, s.CodesWithLeadingSpaces);          // "  2050000117121" y " ABC"
        Assert.Equal(1, s.CodesWithTrailingSpaces);         // "ABC "
        Assert.Equal(15, s.MaxCodeLength);                  // "  2050000117121" = 2 espacios + 13 dígitos
    }

    [Fact]
    public void Codigos_repetidos_hacen_que_los_distintos_sean_menos_que_el_total()
    {
        // Si el JOIN duplicara artículos, DistinctCodes < TotalRows lo delata.
        var s = CatalogSummaryBuilder.From([P("A"), P("A"), P("B")]);

        Assert.Equal(3, s.TotalRows);
        Assert.Equal(2, s.DistinctCodes);
    }

    [Fact]
    public void Clasifica_existencias_raras()
    {
        var s = CatalogSummaryBuilder.From([
            P("1", stock: -3),
            P("2", stock: 2.75),
            P("3", stock: 500_000_000_000),
            P("4", stock: 20_000),
            P("5", stock: 5),
            P("6", stock: null),
        ]);

        Assert.Equal(1, s.StockNegative);        // -3
        Assert.Equal(1, s.StockWithDecimals);    // 2.75 (-3 es entero)
        Assert.Equal(2, s.StockOver10Thousand);  // 500 000 000 000 y 20 000
        Assert.Equal(1, s.StockOver1Million);    // 500 000 000 000
    }

    [Fact]
    public void Mide_longitudes_maximas()
    {
        var s = CatalogSummaryBuilder.From([
            P("12345678", descr: "CORTA"),
            P("1234567890123456", descr: new string('X', 40)),
        ]);

        Assert.Equal(16, s.MaxCodeLength);
        Assert.Equal(40, s.MaxDescriptionLength);
    }

    [Fact]
    public void El_informe_incluye_resumen_tiempos_y_memoria()
    {
        var summary = CatalogSummaryBuilder.From([P("1"), P("2", price: null), P("  2")]);
        var metrics = new AspelReadMetrics
        {
            ConnectDuration = TimeSpan.FromMilliseconds(120),
            QueryDuration = TimeSpan.FromMilliseconds(850),
        };
        var result = new AspelLoadResult([], metrics, 1);

        var report = TestModeRunner.FormatReport(summary, result, TimeSpan.FromSeconds(1), ProcessMemory.Capture());

        Assert.Contains("Total de filas leídas", report);
        Assert.Contains("Activos sin precio en la lista", report);
        Assert.Contains("Con espacios al inicio", report);
        Assert.Contains("Conexión:", report);
        Assert.Contains("Consulta + lectura:", report);
        Assert.Contains("Máximo en memoria física", report);
        Assert.Contains("No se envió ni se guardó nada", report);
    }
}
