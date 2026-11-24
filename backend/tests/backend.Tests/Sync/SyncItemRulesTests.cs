using backend.Sync;

namespace backend.Tests.Sync;

/// <summary>Reglas de docs/business-rules.md aplicadas a cada artículo recibido.</summary>
public class SyncItemRulesTests
{
    private static SyncItemValues Eval(string? code = "A1", string? description = "LECHE ENT 1LT",
        double? stock = 1, string? status = "A", double? price = 10) =>
        SyncItemRules.Evaluate(code, description, stock, status, price);

    [Theory]
    [InlineData(34.999999, "35.00")]
    [InlineData(34.994, "34.99")]
    [InlineData(34.995, "35.00")]
    [InlineData(0.1, "0.10")]
    public void El_precio_se_redondea_a_dos_decimales(double price, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Eval(price: price).PublicPrice);

    [Theory]
    [InlineData(null)]
    [InlineData(-5.0)]
    [InlineData(0.0)]
    public void Sin_precio_o_negativo_se_guarda_cero(double? price) =>
        Assert.Equal(0m, Eval(price: price).PublicPrice);

    [Fact]
    public void Los_precios_y_existencias_de_relleno_de_aspel_caben()
    {
        var values = Eval(price: 15_000_000_000, stock: 500_000_000_000);

        Assert.False(values.IsRejected);
        Assert.Equal(15_000_000_000m, values.PublicPrice);
        Assert.Equal(500_000_000_000m, values.Stock);
    }

    [Theory]
    [InlineData(1e17, 1.0)]
    [InlineData(1.0, 1e17)]
    [InlineData(1.0, -1e17)]
    public void Valores_fuera_de_rango_se_rechazan(double price, double stock) =>
        Assert.True(Eval(price: price, stock: stock).IsRejected);

    [Fact]
    public void La_existencia_conserva_cuatro_decimales_y_puede_ser_negativa()
    {
        Assert.Equal(2.4567m, Eval(stock: 2.45671).Stock);
        Assert.Equal(-3m, Eval(stock: -3).Stock);
        Assert.Equal(0m, Eval(stock: null).Stock);
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("a", true)]
    [InlineData(" A ", true)]
    [InlineData("B", false)]
    [InlineData("X", false)]
    [InlineData(null, false)]
    public void Solo_el_estado_A_es_activo(string? status, bool active) =>
        Assert.Equal(active, Eval(status: status).IsActive);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_codigo_vacio_se_rechaza(string? code) =>
        Assert.True(Eval(code: code).IsRejected);

    [Fact]
    public void Un_codigo_de_mas_de_50_caracteres_se_rechaza() =>
        Assert.True(Eval(code: new string('9', SyncItemRules.MaxCodeLength + 1)).IsRejected);

    [Fact]
    public void Una_descripcion_de_mas_de_255_caracteres_se_rechaza() =>
        Assert.True(Eval(description: new string('X', SyncItemRules.MaxDescriptionLength + 1)).IsRejected);

    [Fact]
    public void La_descripcion_se_guarda_tal_cual_y_nula_pasa_a_vacia()
    {
        Assert.Equal("  LECHE  ", Eval(description: "  LECHE  ").Description);
        Assert.Equal("", Eval(description: null).Description);
    }

    [Fact]
    public void Un_codigo_con_espacios_al_inicio_es_valido() =>
        Assert.False(Eval(code: "  2050000117121").IsRejected);
}
