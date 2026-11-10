using backend.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests.Catalog;

/// <summary>Casos concretos del categorizador y del nombre para mostrar. Los valores esperados salen de catalog.py.</summary>
public class CatalogRulesTests
{
    private static readonly Categorizer Categorizer = CatalogTestData.CreateCategorizer();
    private static readonly DisplayNamer Namer = CatalogTestData.CreateNamer();

    // ------------------------------------------------------------------ normalización

    [Theory]
    [InlineData("Ñandú", "NANDU")]
    [InlineData("niño", "NINO")]
    [InlineData("CAFÉ", "CAFE")]
    [InlineData(null, "")]
    public void Normalize_pasa_a_mayusculas_y_quita_acentos(string? input, string expected) =>
        Assert.Equal(expected, CatalogText.Normalize(input));

    [Fact]
    public void Tokens_separa_por_todo_lo_que_no_sea_letra_o_numero()
    {
        Assert.Equal(["P", "PERRO"], CatalogText.Tokens("P/PERRO"));
        Assert.Equal(["LECHE", "ENT", "1LT"], CatalogText.Tokens("  leche, ent-1LT "));
        Assert.Equal(["NINO", "5"], CatalogText.Tokens("NIÑO_5"));
        Assert.Empty(CatalogText.Tokens("  / - "));
    }

    [Fact]
    public void El_entorno_sabe_quitar_acentos() => CatalogText.EnsureUnicodeSupport();

    // ------------------------------------------------------------------ categorías con las reglas reales

    [Theory]
    [InlineData("LECHE ENT 1LT", "lacteos")]
    [InlineData("GLOBO", "fiesta")]
    [InlineData("GLOBOS", "fiesta")]
    [InlineData("LLAVE DE JARDIN", "ferreteria")]
    [InlineData("LLAVES DE JARDIN", "ferreteria")]
    [InlineData("PAN", "panaderia")]
    [InlineData("PANALES", "bebe")]
    [InlineData("CHOCOLATE CON LECHE", "snacks")]
    [InlineData("ACEITE", "abarrotes")]
    [InlineData("ACEITE MOTOR", "vehiculos")]
    [InlineData("CEPILLO DE DIENTE NIÑO", "cuidado-personal")]
    [InlineData("CAFÉ MOLIDO 6PC", "abarrotes")]
    [InlineData("XQZW FHJ", "otros")]
    [InlineData("ÑAME", "otros")]
    [InlineData("", "otros")]
    [InlineData(null, "otros")]
    public void Categoriza_como_catalog_py(string? description, string expected) =>
        Assert.Equal(expected, Categorizer.Categorize(description).Slug);

    [Fact]
    public void Sin_coincidencia_devuelve_otros_sin_palabra_clave()
    {
        var match = Categorizer.Categorize("QWERTY ASDF");

        Assert.Equal("otros", match.Slug);
        Assert.Null(match.Keyword);
    }

    [Fact]
    public void La_palabra_clave_es_de_palabras_completas_PAN_no_coincide_con_PANAL()
    {
        Assert.Equal("PAN", Categorizer.Categorize("PAN").Keyword);
        Assert.Equal("PANAL", Categorizer.Categorize("PANAL").Keyword);
    }

    // ------------------------------------------------------------------ desempate (reglas propias)

    private static Categorizer Custom(params (string Slug, int Position, (string Keyword, int Priority)[] Rules)[] categories)
    {
        var definitions = categories
            .Select(c => new CategoryDefinition(c.Slug, c.Slug, c.Position,
                c.Rules.Select(r => new RuleDefinition(r.Keyword, r.Priority)).ToList()))
            .Append(new CategoryDefinition("otros", "Otros", 1000, []))
            .ToList();

        return new Categorizer(new CategoryRuleSet(definitions));
    }

    [Fact]
    public void Desempata_por_mayor_prioridad()
    {
        var categorizer = Custom(("a", 1, [("SOL", 20)]), ("b", 2, [("LUNA", 30)]));

        Assert.Equal("b", categorizer.Categorize("SOL LUNA").Slug);
        Assert.Equal("b", categorizer.Categorize("LUNA SOL").Slug);
    }

    [Fact]
    public void Con_la_misma_prioridad_gana_la_palabra_clave_mas_larga()
    {
        var categorizer = Custom(("a", 1, [("LECHE", 20)]), ("b", 2, [("CHOCOLATE", 20)]));

        Assert.Equal("b", categorizer.Categorize("CHOCOLATE CON LECHE").Slug);
    }

    [Fact]
    public void Con_prioridad_y_largo_iguales_gana_la_categoria_de_menor_posicion()
    {
        var categorizer = Custom(("a", 2, [("UNO", 20)]), ("b", 1, [("DOS", 20)]));

        Assert.Equal("b", categorizer.Categorize("UNO DOS").Slug);
    }

    [Fact]
    public void El_largo_se_cuenta_sin_espacios()
    {
        // "AB CD EF" (6 letras) contra "ABCDEFG" (7 letras): gana la de 7 aunque tenga menos palabras.
        var categorizer = Custom(("a", 1, [("AB CD EF", 20)]), ("b", 2, [("ABCDEFG", 20)]));

        Assert.Equal("b", categorizer.Categorize("AB CD EF ABCDEFG").Slug);
    }

    [Fact]
    public void Las_palabras_de_la_clave_tienen_que_venir_seguidas()
    {
        var categorizer = Custom(("a", 1, [("AGUA MINERAL", 20)]));

        Assert.Equal("a", categorizer.Categorize("AGUA MINERAL 1LT").Slug);
        Assert.Equal("otros", categorizer.Categorize("AGUA FRIA MINERAL").Slug);
    }

    [Fact]
    public void Acepta_plural_con_S_o_ES_en_cada_palabra_pero_no_otras_terminaciones()
    {
        var categorizer = Custom(("a", 1, [("CARTEL", 20), ("LLAVE", 20)]));

        Assert.Equal("a", categorizer.Categorize("CARTELES").Slug);
        Assert.Equal("a", categorizer.Categorize("LLAVES").Slug);
        Assert.Equal("otros", categorizer.Categorize("LLAVERO").Slug);
    }

    // ------------------------------------------------------------------ validación de las reglas

    [Fact]
    public void Rechaza_reglas_mal_formadas()
    {
        static CategoryRuleSet Build(params CategoryDefinition[] categories) => new(categories);
        var others = new CategoryDefinition("otros", "Otros", 1000, []);

        Assert.Throws<InvalidOperationException>(() => Build(
            new CategoryDefinition("a", "A", 1, [new RuleDefinition("Leche", 10)]), others));
        Assert.Throws<InvalidOperationException>(() => Build(
            new CategoryDefinition("a", "A", 1, [new RuleDefinition("LECHE", 10)]),
            new CategoryDefinition("b", "B", 2, [new RuleDefinition("LECHE", 10)]), others));
        Assert.Throws<InvalidOperationException>(() => Build(
            new CategoryDefinition("a", "A", 1, [new RuleDefinition("LECHE", 10)])));
    }

    // ------------------------------------------------------------------ nombre para mostrar

    [Theory]
    [InlineData("LECHE ENT 1LT", "Leche Entera 1 L")]
    [InlineData("LECHE ENTERA LEYDE , 473ML", "Leche Entera Leyde, 473 ml")]
    [InlineData("PINTURA COLORAMICA ACEITE 1/4_GLN AMA TR", "Pintura Coloramica Aceite 1/4 gal Amarilla Tr")]
    [InlineData("CEPILLO DE DIENTE NIÑO", "Cepillo de Diente Niño")]
    [InlineData("PIÑA", "Piña")]
    [InlineData("BROCA HIERRO 5/32\" COBAL DEWALT DW1210", "Broca Hierro 5/32\" Cobal Dewalt DW1210")]
    [InlineData("MINI COCA/BANANA/UVA/SPRITE", "Mini Coca/Banana/Uva/Sprite")]
    [InlineData("BOLSA V/C 5LB", "Bolsa 5 lb")]
    [InlineData("BOLSA V/D", "Bolsa")]
    [InlineData("VELA PEQ", "Vela Pequeña")]
    [InlineData("CAMISA PEQ", "Camisa Pequeña")]
    [InlineData("ESCOBA P/PERRO C/U", "Escoba para Perro C/U")]
    [InlineData("TAPA S/R", "Tapa sin Rosca")]
    [InlineData("MEDIAS C/ENCAJE", "Medias con Encaje")]
    [InlineData("1/2LIBRA JAMON", "1/2 lb Jamón")]
    [InlineData("CAFÉ MOLIDO 6PC", "Café Molido 6 piezas")]
    [InlineData("IPHONE 12 USB", "iPhone 12 USB")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Nombre_para_mostrar_como_catalog_py(string? description, string expected) =>
        Assert.Equal(expected, Namer.GetDisplayName(description));

    [Fact]
    public void La_abreviatura_con_genero_sigue_a_la_primera_palabra()
    {
        // VELA termina en A → femenino; LAPIZ no → masculino.
        Assert.Equal("Vela Pequeña", Namer.GetDisplayName("VELA PEQ"));
        Assert.Equal("Lápiz Pequeño", Namer.GetDisplayName("LAPIZ PEQ"));
    }

    // ------------------------------------------------------------------ inyección (la usará US-06)

    [Fact]
    public void AddCatalogRules_deja_los_servicios_listos_para_inyectar()
    {
        using var provider = new ServiceCollection().AddCatalogRules().BuildServiceProvider();

        Assert.Equal("lacteos", provider.GetRequiredService<ICategorizer>().Categorize("LECHE ENT 1LT").Slug);
        Assert.Equal("Leche Entera 1 L", provider.GetRequiredService<IDisplayNamer>().GetDisplayName("LECHE ENT 1LT"));
    }
}
