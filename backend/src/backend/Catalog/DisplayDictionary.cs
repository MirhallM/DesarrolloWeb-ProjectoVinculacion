using System.Text.Json;

namespace backend.Catalog;

/// <summary>
/// Diccionario del nombre para mostrar (Data/Seed/display-dictionary.json). No tiene tabla:
/// sale del JSON embebido, igual que lo lee catalog.py.
/// </summary>
public sealed class DisplayDictionary
{
    internal const string EmbeddedName = "backend.Seed.display-dictionary.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Abreviatura → expansión (masculino y, si existe, femenino).</summary>
    public Dictionary<string, string[]> Abbreviations { get; init; } = [];

    /// <summary>Palabra sin acento → palabra con acento.</summary>
    public Dictionary<string, string> Accents { get; init; } = [];

    /// <summary>Unidad de Aspel → unidad para mostrar (LT → L).</summary>
    public Dictionary<string, string> Units { get; init; } = [];

    public List<string> Acronyms { get; init; } = [];

    public Dictionary<string, string> Special { get; init; } = [];

    /// <summary>Artículos y preposiciones: van en minúscula salvo al inicio.</summary>
    public List<string> LowerWords { get; init; } = [];

    /// <summary>Palabras femeninas que no terminan en A.</summary>
    public List<string> FeminineWords { get; init; } = [];

    /// <summary>Palabras que terminan en A pero son masculinas.</summary>
    public List<string> FeminineExceptions { get; init; } = [];

    public static DisplayDictionary LoadEmbedded() => Parse(CategoryRuleSet.ReadEmbeddedText(EmbeddedName));

    public static DisplayDictionary Parse(string json) =>
        JsonSerializer.Deserialize<DisplayDictionary>(json, JsonOptions)
            ?? throw new InvalidOperationException("display-dictionary.json está vacío.");
}
