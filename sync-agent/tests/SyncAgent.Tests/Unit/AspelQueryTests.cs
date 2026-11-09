using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using SyncAgent.Aspel;

namespace SyncAgent.Tests.Unit;

/// <summary>Guardas para que la consulta del agente siga siendo de solo lectura y no pierda productos.</summary>
public class AspelQueryTests
{
    [Fact]
    public void Es_un_select()
    {
        Assert.StartsWith("SELECT", AspelQuery.Sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("MERGE")]
    [InlineData("DROP")]
    [InlineData("ALTER")]
    [InlineData("TRUNCATE")]
    [InlineData("EXEC")]
    [InlineData("INTO")]
    [InlineData("NOLOCK")]
    public void No_contiene_palabras_de_escritura_ni_NOLOCK(string word)
    {
        Assert.DoesNotMatch(new Regex($@"\b{word}\b", RegexOptions.IgnoreCase), AspelQuery.Sql);
    }

    [Fact]
    public void Usa_LEFT_JOIN_para_no_perder_productos_sin_precio()
    {
        Assert.Matches(new Regex(@"\bLEFT\s+JOIN\b", RegexOptions.IgnoreCase), AspelQuery.Sql);
    }

    [Fact]
    public void La_lista_de_precios_es_un_parametro()
    {
        Assert.Contains($"CVE_PRECIO = {AspelQuery.PriceListParameter}", AspelQuery.Sql);
    }

    [Fact]
    public void No_hace_trim_de_las_columnas()
    {
        Assert.DoesNotMatch(new Regex(@"\b(LTRIM|RTRIM|TRIM)\b", RegexOptions.IgnoreCase), AspelQuery.Sql);
    }
}

public class AspelOptionsTests
{
    [Fact]
    public void Los_valores_por_defecto_son_validos()
    {
        Assert.Empty(new AspelOptions().Validate());
    }

    private static AspelOptions Load(params (string Key, string Value)[] values) =>
        AspelOptions.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build());

    [Fact]
    public void Sin_configuracion_usa_las_esperas_de_fabrica()
    {
        Assert.Equal([5, 15, 45], Load().Retry.DelaysSeconds);
        Assert.Equal(4, Load().Retry.MaxAttempts);
    }

    [Fact]
    public void Las_esperas_configuradas_reemplazan_a_las_de_fabrica_y_no_se_agregan()
    {
        var options = Load(
            ("Aspel:Retry:DelaysSeconds:0", "0"),
            ("Aspel:Retry:DelaysSeconds:1", "1"),
            ("Aspel:Retry:DelaysSeconds:2", "2"));

        Assert.Equal([0, 1, 2], options.Retry.DelaysSeconds); // no [5,15,45,0,1,2]
    }

    [Fact]
    public void Lee_el_resto_de_valores_de_la_seccion()
    {
        var options = Load(("Aspel:PriceList", "4"), ("Aspel:CommandTimeoutSeconds", "90"), ("Aspel:Retry:MaxAttempts", "2"));

        Assert.Equal(4, options.PriceList);
        Assert.Equal(90, options.CommandTimeoutSeconds);
        Assert.Equal(2, options.Retry.MaxAttempts);
    }

    [Fact]
    public void Detecta_valores_invalidos()
    {
        var options = new AspelOptions { PriceList = 0, CommandTimeoutSeconds = 0 };

        Assert.Equal(2, options.Validate().Count);
    }
}

public class AspelConnectionStringTests
{
    [Fact]
    public void Agrega_nombre_de_aplicacion_solo_lectura_y_timeout_sin_perder_los_datos_originales()
    {
        var result = AspelProductReader.BuildConnectionString(
            "Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password=secreta;TrustServerCertificate=True;",
            new AspelOptions { ConnectTimeoutSeconds = 7 });

        Assert.Contains("NettesSyncAgent", result);
        Assert.Contains("ReadOnly", result);
        Assert.Contains("Connect Timeout=7", result);
        Assert.Contains("SAE70Empre01", result);
    }
}
