using backend.Catalog;

namespace backend.Tests.Catalog;

/// <summary>La huella de las reglas decide si una corrida "sin cambios" es válida después de un cambio de reglas o de un despliegue.</summary>
public sealed class CatalogRulesFingerprintTests
{
    private const string Categories = """{"categories":[]}""";
    private const string Dictionary = """{"units":{"LT":"L"}}""";

    private static string Compute(string categories = Categories, string dictionary = Dictionary, string version = "1", string? sha = null) =>
        CatalogRulesFingerprint.Compute(categories, dictionary, version, sha);

    [Fact]
    public void Las_mismas_entradas_dan_la_misma_huella_en_hexadecimal_de_64_caracteres()
    {
        var a = Compute(sha: "abc123");

        Assert.Equal(a, Compute(sha: "abc123"));
        Assert.Equal(64, a.Length);
        Assert.All(a, c => Assert.True(Uri.IsHexDigit(c) && !char.IsUpper(c)));
    }

    [Fact]
    public void Cambia_si_cambia_cualquiera_de_las_cuatro_partes()
    {
        var baseline = Compute(sha: "abc123");

        Assert.NotEqual(baseline, Compute(categories: """{"categories":[ ]}""", sha: "abc123")); // un byte del JSON de categorías
        Assert.NotEqual(baseline, Compute(dictionary: """{"units":{"LT":"l"}}""", sha: "abc123")); // un byte del diccionario
        Assert.NotEqual(baseline, Compute(version: "2", sha: "abc123"));
        Assert.NotEqual(baseline, Compute(sha: "abc124"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_commit_da_lo_mismo_que_no_tenerlo(string? sha)
    {
        Assert.Equal(Compute(sha: null), Compute(sha: sha));
        Assert.NotEqual(Compute(sha: null), Compute(sha: "abc123"));
    }

    [Fact]
    public void El_commit_se_compara_sin_espacios_en_los_extremos()
    {
        Assert.Equal(Compute(sha: "abc123"), Compute(sha: " abc123\n"));
    }

    [Fact]
    public void Mover_texto_de_una_parte_a_otra_no_da_la_misma_huella()
    {
        // Sin separar las partes por su largo, "ab" + "c" y "a" + "bc" producirían el mismo texto.
        Assert.NotEqual(
            CatalogRulesFingerprint.Compute("ab", "c", "1", null),
            CatalogRulesFingerprint.Compute("a", "bc", "1", null));
    }

    [Fact]
    public void Con_los_json_embebidos_la_huella_es_estable_y_usa_el_commit_del_entorno()
    {
        // En local y en el CI no existe RAILWAY_GIT_COMMIT_SHA; la prueba no depende de cuál sea el valor.
        var first = CatalogRulesFingerprint.FromEnvironment();
        var second = CatalogRulesFingerprint.FromEnvironment();

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(
            CatalogRulesFingerprint.Compute(
                CatalogTestData.CategoriesJson,
                CatalogTestData.DictionaryJson,
                CatalogRulesFingerprint.AlgorithmVersion,
                Environment.GetEnvironmentVariable(CatalogRulesFingerprint.CommitVariable)),
            first.Value);
    }
}
