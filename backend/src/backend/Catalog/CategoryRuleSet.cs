using System.Reflection;
using System.Text.Json;

namespace backend.Catalog;

public sealed record RuleDefinition(string Keyword, int Priority);

public sealed record CategoryDefinition(string Slug, string Name, int Position, IReadOnlyList<RuleDefinition> Rules);

/// <summary>
/// Categorías y reglas de palabras clave (Data/Seed/categories.json). El JSON va embebido en el
/// ensamblado, así el categorizador no depende de la base de datos y usa la misma fuente que catalog.py.
/// </summary>
public sealed class CategoryRuleSet
{
    /// <summary>Slug de la categoría para los productos que no coinciden con ninguna regla.</summary>
    public const string OthersSlug = "otros";

    internal const string EmbeddedCategoriesName = "backend.Seed.categories.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CategoryRuleSet(IReadOnlyList<CategoryDefinition> categories)
    {
        Validate(categories);
        Categories = categories;
    }

    public IReadOnlyList<CategoryDefinition> Categories { get; }

    /// <summary>Lee categories.json embebido en el ensamblado.</summary>
    public static CategoryRuleSet LoadEmbedded() => Parse(ReadEmbeddedText(EmbeddedCategoriesName));

    /// <summary>Lee el JSON de categorías desde texto (las pruebas lo usan con reglas alteradas).</summary>
    public static CategoryRuleSet Parse(string json)
    {
        var file = JsonSerializer.Deserialize<CategoriesFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("categories.json está vacío.");

        var categories = (file.Categories ?? [])
            .Select(c => new CategoryDefinition(
                c.Slug ?? string.Empty,
                c.Name ?? string.Empty,
                c.Position,
                (c.Rules ?? []).Select(r => new RuleDefinition(r.Keyword ?? string.Empty, r.Priority)).ToList()))
            .ToList();

        return new CategoryRuleSet(categories);
    }

    internal static string ReadEmbeddedText(string resourceName)
    {
        using var stream = typeof(CategoryRuleSet).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido {resourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Validate(IReadOnlyList<CategoryDefinition> categories)
    {
        if (!categories.Any(c => c.Slug == OthersSlug))
        {
            throw new InvalidOperationException($"Falta la categoría '{OthersSlug}'.");
        }

        EnsureUnique(categories.Select(c => c.Slug), "slug");
        EnsureUnique(categories.Select(c => c.Name), "nombre de categoría");
        EnsureUnique(categories.Select(c => c.Position.ToString()), "posición");
        EnsureUnique(categories.SelectMany(c => c.Rules).Select(r => r.Keyword), "palabra clave");

        foreach (var rule in categories.SelectMany(c => c.Rules))
        {
            // La clave debe venir ya normalizada: la tabla category_rules la guarda tal cual
            // y se compara contra la descripción normalizada.
            if (string.Join(' ', CatalogText.Tokens(rule.Keyword)) != rule.Keyword || rule.Keyword.Length == 0)
            {
                throw new InvalidOperationException(
                    $"La palabra clave '{rule.Keyword}' no está normalizada (mayúsculas, sin acentos y separada por un espacio).");
            }
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string what)
    {
        var repeated = values.GroupBy(v => v, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
        {
            throw new InvalidOperationException($"Hay más de un {what} '{repeated.Key}' en categories.json.");
        }
    }

    private sealed class CategoriesFile
    {
        public List<CategoryEntry>? Categories { get; set; }
    }

    private sealed class CategoryEntry
    {
        public string? Slug { get; set; }

        public string? Name { get; set; }

        public int Position { get; set; }

        public List<RuleEntry>? Rules { get; set; }
    }

    private sealed class RuleEntry
    {
        public string? Keyword { get; set; }

        public int Priority { get; set; }
    }
}
