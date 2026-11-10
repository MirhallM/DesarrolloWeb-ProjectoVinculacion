using System.Globalization;
using System.Text.RegularExpressions;

namespace backend.Catalog;

public interface IDisplayNamer
{
    string GetDisplayName(string? description);
}

/// <summary>
/// Genera el nombre para mostrar a partir de la descripción de Aspel. Port de DisplayNamer en
/// tools/catalog-analysis/catalog.py: si algo difiere, el comportamiento correcto es el de catalog.py.
/// </summary>
/// <remarks>
/// Regex: \b, \d y \s son Unicode tanto en Python como en .NET, y los lookbehind/lookahead se escriben igual.
/// Se usa \z en lugar de $ para anclar el final; las palabras no llevan espacios, así que es lo mismo.
/// </remarks>
public sealed class DisplayNamer : IDisplayNamer
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private const string NumberPattern = @"\d+(?:[.,/]\d+)?";

    private static readonly Regex StripVcVd = new(@"\bV/[CD]\b", Options);
    private static readonly Regex KeepUnitPrice = new(@"\bC/U\b", Options);
    private static readonly Regex SinRosca = new(@"\bS/R\b", Options);
    private static readonly Regex Commas = new(@"\s*,\s*", Options);
    private static readonly Regex Whitespace = new(@"\s+", Options);
    private static readonly Regex Digit = new(@"\d", Options);
    private static readonly Regex Number = new($@"^{NumberPattern}\z", Options);
    private static readonly Regex SlashWords = new(@"^[A-ZÑÁÉÍÓÚ]+(?:/[A-ZÑÁÉÍÓÚ]+)+\z", Options);

    // P/ → para, C/ → con (salvo C/U), S/ → sin, D/ → de; solo al inicio o tras espacio o "/".
    private static readonly (Regex Pattern, string Replacement)[] Prefixes =
    [
        (new Regex(@"(?:(?<=^)|(?<=[\s/]))P/", Options), " PARA "),
        (new Regex(@"(?:(?<=^)|(?<=[\s/]))C/(?!U\b)", Options), " CON "),
        (new Regex(@"(?:(?<=^)|(?<=[\s/]))S/", Options), " SIN "),
        (new Regex(@"(?:(?<=^)|(?<=[\s/]))D/", Options), " DE "),
    ];

    private readonly Dictionary<string, string[]> _abbreviations;
    private readonly Dictionary<string, string> _accents;
    private readonly Dictionary<string, string> _units;
    private readonly HashSet<string> _acronyms;
    private readonly Dictionary<string, string> _special;
    private readonly HashSet<string> _lower;
    private readonly HashSet<string> _feminineWords;
    private readonly HashSet<string> _feminineExceptions;
    private readonly Regex _numberUnit;

    public DisplayNamer(DisplayDictionary dictionary)
    {
        CatalogText.EnsureUnicodeSupport();

        _abbreviations = dictionary.Abbreviations;
        _accents = dictionary.Accents;
        _units = dictionary.Units;
        _special = dictionary.Special;
        _acronyms = [.. dictionary.Acronyms];
        _lower = [.. dictionary.LowerWords];
        _feminineWords = [.. dictionary.FeminineWords];
        _feminineExceptions = [.. dictionary.FeminineExceptions];

        // Unidades de la más larga a la más corta, como en catalog.py.
        var alternatives = string.Join('|', _units.Keys.OrderByDescending(u => u.Length).Select(Regex.Escape));
        _numberUnit = new Regex($@"^({NumberPattern})({alternatives})\z", Options);
    }

    public string GetDisplayName(string? description)
    {
        var s = (description ?? string.Empty).Trim().ToUpperInvariant().Replace('_', ' ');
        s = StripVcVd.Replace(s, " ");
        s = KeepUnitPrice.Replace(s, " C/U ");
        s = SinRosca.Replace(s, " SIN ROSCA ");

        foreach (var (pattern, replacement) in Prefixes)
        {
            s = pattern.Replace(s, replacement);
        }

        s = Commas.Replace(s, ", ");

        var words = Whitespace.Split(s).Where(w => w.Length > 0).ToList();
        var fem = IsFeminine(words.FirstOrDefault(IsAlpha) ?? string.Empty);
        var output = new List<string>();

        foreach (var word in words)
        {
            var core = word.Trim('.', ',', ';', ':', '/');
            var tail = word.EndsWith(',') ? "," : string.Empty;

            if (core.Length == 0)
            {
                continue;
            }

            var match = _numberUnit.Match(core);
            if (match.Success)
            {
                output.Add(match.Groups[1].Value);
                output.Add(_units[match.Groups[2].Value] + tail);
                continue;
            }

            if (_units.TryGetValue(core, out var unit) && output.Count > 0 && Number.IsMatch(output[^1]))
            {
                output.Add(unit + tail);
                continue;
            }

            var parts = SlashWords.IsMatch(core) ? core.Split('/') : [core];
            var first = output.Count == 0;
            var converted = parts.Select((part, i) => ConvertWord(part, fem, first && i == 0));
            output.Add(string.Join('/', converted) + tail);
        }

        return string.Join(' ', output).Trim(' ', ',');
    }

    private bool IsFeminine(string word) =>
        _feminineWords.Contains(word) || (word.EndsWith('A') && !_feminineExceptions.Contains(word));

    private string ConvertWord(string core, bool fem, bool first)
    {
        string w;

        if (_abbreviations.TryGetValue(core, out var expansion))
        {
            w = fem && expansion.Length > 1 ? expansion[1] : expansion[0];
        }
        else if (_special.TryGetValue(core, out var special))
        {
            return special;
        }
        else if (_acronyms.Contains(core) || Digit.IsMatch(core))
        {
            return core;
        }
        else if (_accents.TryGetValue(core, out var accented))
        {
            w = accented;
        }
        else
        {
            w = core.ToLowerInvariant();
        }

        if (_lower.Contains(w) && !first)
        {
            return w;
        }

        return char.IsLower(w[0]) ? char.ToUpperInvariant(w[0]) + w[1..] : w;
    }

    /// <summary>Equivale a str.isalpha() de Python: no vacía y solo letras.</summary>
    private static bool IsAlpha(string word) => word.Length > 0 && word.All(char.IsLetter);
}
