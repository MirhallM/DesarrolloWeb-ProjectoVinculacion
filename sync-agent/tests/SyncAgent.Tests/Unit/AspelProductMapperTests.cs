using System.Data;
using SyncAgent.Aspel;

namespace SyncAgent.Tests.Unit;

public class AspelProductMapperTests
{
    /// <summary>Crea un lector en memoria con las mismas 5 columnas (y orden) que AspelQuery.</summary>
    private static IDataReader ReaderWith(
        object? code, object? description, object? stock, object? status, object? price,
        Type? stockType = null, Type? priceType = null)
    {
        var table = new DataTable();
        table.Columns.Add("CVE_ART", typeof(string));
        table.Columns.Add("DESCR", typeof(string));
        table.Columns.Add("EXIST", stockType ?? typeof(double));
        table.Columns.Add("STATUS", typeof(string));
        table.Columns.Add("PRECIO", priceType ?? typeof(double));
        table.Rows.Add(code ?? DBNull.Value, description ?? DBNull.Value, stock ?? DBNull.Value,
            status ?? DBNull.Value, price ?? DBNull.Value);

        var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        return reader;
    }

    [Fact]
    public void Producto_normal_se_mapea_completo()
    {
        using var reader = ReaderWith("7501", "LECHE ENT 1LT", 12.0, "A", 34.5);

        var product = AspelProductMapper.Map(reader);

        Assert.Equal(new AspelProduct("7501", "LECHE ENT 1LT", 12.0, "A", 34.5), product);
    }

    [Fact]
    public void Precio_nulo_se_conserva_como_null_y_no_como_cero()
    {
        // LEFT JOIN: el artículo sin precio en la lista llega con PRECIO = NULL.
        using var reader = ReaderWith("7502", "SIN PRECIO", 5.0, "A", null);

        var product = AspelProductMapper.Map(reader);

        Assert.Null(product.Price);
    }

    [Theory]
    [InlineData("  2050000117121")]
    [InlineData("2050000117121")]
    [InlineData("2050000117121  ")]
    [InlineData(" A B ")]
    public void Codigo_con_espacios_queda_intacto(string code)
    {
        using var reader = ReaderWith(code, "X", 1.0, "A", 1.0);

        var product = AspelProductMapper.Map(reader);

        Assert.Equal(code, product.Code); // sin recortar ni normalizar
    }

    [Fact]
    public void Los_codigos_que_difieren_solo_por_espacios_siguen_siendo_distintos()
    {
        using var a = ReaderWith("  2050000117121", "UNO", 1.0, "A", 1.0);
        using var b = ReaderWith("2050000117121", "DOS", 1.0, "A", 1.0);

        Assert.NotEqual(AspelProductMapper.Map(a).Code, AspelProductMapper.Map(b).Code);
    }

    [Theory]
    [InlineData(-3.0)]
    [InlineData(2.75)]
    [InlineData(500000000000.0)]
    public void Existencia_negativa_decimal_o_inflada_se_entrega_tal_cual(double stock)
    {
        using var reader = ReaderWith("C1", "X", stock, "A", 1.0);

        Assert.Equal(stock, AspelProductMapper.Map(reader).Stock);
    }

    [Fact]
    public void Precio_con_decimales_de_mas_no_se_redondea()
    {
        using var reader = ReaderWith("C1", "X", 1.0, "A", 34.999999);

        Assert.Equal(34.999999, AspelProductMapper.Map(reader).Price);
    }

    [Fact]
    public void Descripcion_vacia_literal_NULL_y_nula_se_distinguen()
    {
        using var vacia = ReaderWith("C1", "", 1.0, "A", 1.0);
        using var textoNull = ReaderWith("C2", "NULL", 1.0, "A", 1.0);
        using var nula = ReaderWith("C3", null, 1.0, "A", 1.0);

        Assert.Equal("", AspelProductMapper.Map(vacia).Description);
        Assert.Equal("NULL", AspelProductMapper.Map(textoNull).Description);
        Assert.Null(AspelProductMapper.Map(nula).Description);
    }

    [Fact]
    public void Columnas_decimal_y_real_tambien_se_convierten_a_double()
    {
        // Si en la tienda EXIST/PRECIO resultan ser decimal o real en vez de float, debe seguir funcionando.
        using var reader = ReaderWith("C1", "X", 2.5m, "A", 10.25f, typeof(decimal), typeof(float));

        var product = AspelProductMapper.Map(reader);

        Assert.Equal(2.5, product.Stock);
        Assert.Equal(10.25, product.Price);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void Estado_se_entrega_tal_cual(string status)
    {
        using var reader = ReaderWith("C1", "X", 1.0, status, 1.0);

        Assert.Equal(status, AspelProductMapper.Map(reader).Status);
    }
}
