using System.Globalization;
using System.Text;
using backend.Tests.Catalog;

namespace backend.Tests.Integration;

/// <summary>
/// Cobertura con el inventario completo de Aspel (el CSV de tools/catalog-analysis/data, que no se sube al repositorio).
/// Se omite si no está definida la variable CATALOG_INVENTORY_CSV. Compara con "analyze.py": mismos filtros de visibilidad.
/// </summary>
[Trait("Category", "Integration")]
public class InventoryCoverageTests
{
    private const double MaxPrice = 100_000;

    [Fact]
    public void El_porcentaje_de_productos_categorizados_es_99_9()
    {
        var path = Environment.GetEnvironmentVariable(IntegrationEnvironment.InventoryVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path),
            $"Define {IntegrationEnvironment.InventoryVariable} con la ruta del CSV de inventario (tools/catalog-analysis/data/inventario.csv).");

        var categorizer = CatalogTestData.CreateCategorizer();
        int visible = 0, categorized = 0;

        foreach (var (description, status, price) in ReadInventory(path!))
        {
            var trimmed = description.Trim();
            if (status != "A" || !(price > 0 && price < MaxPrice) || trimmed.Length == 0 || trimmed.ToUpperInvariant() == "NULL")
            {
                continue;
            }

            visible++;
            if (categorizer.Categorize(description).Slug != "otros")
            {
                categorized++;
            }
        }

        var percent = 100.0 * categorized / visible;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Visibles: {visible}. Categorizados: {categorized} ({percent.ToString("F1", CultureInfo.InvariantCulture)} %).");

        Assert.Equal(99.9, Math.Round(percent, 1));
    }

    // SSMS exporta separado por ";", sin encabezado: CVE_ART; DESCR; EXIST; STATUS; PRECIO.
    private static IEnumerable<(string Description, string Status, double Price)> ReadInventory(string path)
    {
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = SplitFields(line.TrimStart('﻿'));
            while (fields.Count < 5)
            {
                fields.Add(string.Empty);
            }

            var priceText = fields[4].Trim();
            var price = priceText is "" or "NULL" ? 0 : double.Parse(priceText, CultureInfo.InvariantCulture);
            yield return (fields[1], fields[3].Trim(), price);
        }
    }

    private static List<string> SplitFields(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ';')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
