using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using backend.Catalog;

namespace backend.Tests.Catalog;

/// <summary>Un caso del archivo de paridad: lo que catalog.py calculó para una descripción de Aspel.</summary>
public sealed record ParityCase(string Description, string Category, string DisplayName);

public sealed record ParityDifference(ParityCase Expected, string ActualCategory, string ActualDisplayName);

public static class ParityComparer
{
    public const int MaxShown = 20;

    public static List<ParityDifference> FindDifferences(
        IEnumerable<ParityCase> cases, ICategorizer categorizer, IDisplayNamer namer)
    {
        var differences = new List<ParityDifference>();

        foreach (var expected in cases)
        {
            var category = categorizer.Categorize(expected.Description).Slug;
            var displayName = namer.GetDisplayName(expected.Description);

            if (category != expected.Category || displayName != expected.DisplayName)
            {
                differences.Add(new ParityDifference(expected, category, displayName));
            }
        }

        return differences;
    }

    /// <summary>Texto para el mensaje de la prueba: el total y los primeros 20 casos distintos, no solo el primero.</summary>
    public static string Describe(IReadOnlyList<ParityDifference> differences, int total)
    {
        var text = new StringBuilder();
        text.AppendLine($"{differences.Count} de {total} casos difieren de catalog.py (se muestran los primeros {Math.Min(MaxShown, differences.Count)}):");

        foreach (var d in differences.Take(MaxShown))
        {
            text.AppendLine($"  descripción : \"{d.Expected.Description}\"");
            if (d.Expected.Category != d.ActualCategory)
            {
                text.AppendLine($"    categoría : esperada {d.Expected.Category} | C# {d.ActualCategory}");
            }

            if (d.Expected.DisplayName != d.ActualDisplayName)
            {
                text.AppendLine($"    nombre    : esperado \"{d.Expected.DisplayName}\" | C# \"{d.ActualDisplayName}\"");
            }
        }

        return text.ToString();
    }
}

/// <summary>Datos compartidos por las pruebas del catálogo.</summary>
public static class CatalogTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Categorizer CreateCategorizer() => new(CategoryRuleSet.LoadEmbedded());

    public static DisplayNamer CreateNamer() => new(DisplayDictionary.LoadEmbedded());

    /// <summary>categories.json o display-dictionary.json tal como van dentro del ensamblado.</summary>
    public static string EmbeddedJson(string resourceName)
    {
        using var stream = typeof(CategoryRuleSet).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"No existe el recurso {resourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string CategoriesJson => EmbeddedJson("backend.Seed.categories.json");

    public static string DictionaryJson => EmbeddedJson("backend.Seed.display-dictionary.json");

    public static List<ParityCase> LoadParityCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalog", "Data", "catalog-parity-cases.jsonl");

        return File.ReadLines(path, Encoding.UTF8)
            .Where(line => line.Length > 0)
            .Select(line => JsonSerializer.Deserialize<ParityCase>(line, JsonOptions)!)
            .ToList();
    }

    /// <summary>Copia de categories.json con cada regla pasada por <paramref name="edit"/> (para romper algo a propósito).</summary>
    public static string EditRules(string json, Action<JsonNode, JsonObject> edit)
    {
        var root = JsonNode.Parse(json)!;
        foreach (var category in root["categories"]!.AsArray())
        {
            foreach (var rule in category!["rules"]!.AsArray())
            {
                edit(category, rule!.AsObject());
            }
        }

        return root.ToJsonString();
    }
}
