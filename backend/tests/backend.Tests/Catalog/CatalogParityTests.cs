using System.Text.Json.Nodes;
using backend.Catalog;

namespace backend.Tests.Catalog;

/// <summary>
/// Compara el C# con catalog.py usando todas las descripciones únicas del inventario real
/// (tools/catalog-analysis/parity_cases.py). Si algo difiere, el correcto es catalog.py.
/// </summary>
public class CatalogParityTests
{
    [Fact]
    public void El_archivo_de_casos_tiene_el_inventario_completo_y_sin_repetidos()
    {
        var cases = CatalogTestData.LoadParityCases();

        Assert.True(cases.Count > 10_000, $"El archivo de paridad parece incompleto: {cases.Count} casos.");
        Assert.Equal(cases.Count, cases.Select(c => c.Description).Distinct().Count());
    }

    [Fact]
    public void Categoria_y_nombre_para_mostrar_son_los_mismos_que_en_catalog_py()
    {
        var cases = CatalogTestData.LoadParityCases();

        var differences = ParityComparer.FindDifferences(
            cases, CatalogTestData.CreateCategorizer(), CatalogTestData.CreateNamer());

        if (differences.Count > 0)
        {
            Assert.Fail(ParityComparer.Describe(differences, cases.Count));
        }
    }

    // Las dos siguientes comprueban que la prueba de paridad protege: si se cambia a propósito
    // una prioridad o una abreviatura, la comparación tiene que encontrar diferencias.

    [Fact]
    public void La_paridad_detecta_un_cambio_de_prioridad()
    {
        var json = CatalogTestData.EditRules(CatalogTestData.CategoriesJson, (_, rule) =>
        {
            if ((string?)rule["keyword"] == "LECHE")
            {
                rule["priority"] = 99;
            }
        });

        var categorizer = new Categorizer(CategoryRuleSet.Parse(json));

        var differences = ParityComparer.FindDifferences(
            CatalogTestData.LoadParityCases(), categorizer, CatalogTestData.CreateNamer());

        Assert.NotEmpty(differences);
    }

    [Fact]
    public void La_paridad_detecta_un_cambio_de_abreviatura()
    {
        var dictionary = JsonNode.Parse(CatalogTestData.DictionaryJson)!;
        dictionary["abbreviations"]!["ENT"] = new JsonArray("entero");

        var namer = new DisplayNamer(DisplayDictionary.Parse(dictionary.ToJsonString()));

        var differences = ParityComparer.FindDifferences(
            CatalogTestData.LoadParityCases(), CatalogTestData.CreateCategorizer(), namer);

        Assert.NotEmpty(differences);
        Assert.All(differences, d => Assert.Contains("Entero", d.ActualDisplayName));
    }

    [Fact]
    public void El_mensaje_muestra_los_primeros_20_casos_distintos_y_no_solo_el_primero()
    {
        var cases = Enumerable.Range(0, 35)
            .Select(i => new ParityCase($"PRODUCTO RARO {i}", "abarrotes", "Nombre esperado"))
            .ToList();

        var differences = ParityComparer.FindDifferences(
            cases, CatalogTestData.CreateCategorizer(), CatalogTestData.CreateNamer());
        var message = ParityComparer.Describe(differences, cases.Count);

        Assert.Equal(35, differences.Count);
        Assert.Contains("35 de 35", message);
        Assert.Contains("PRODUCTO RARO 19", message);
        Assert.DoesNotContain("PRODUCTO RARO 20", message);
    }
}
