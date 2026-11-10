namespace backend.Catalog;

/// <summary>Categoría elegida y la palabra clave que la decidió (null si cae en "otros").</summary>
public sealed record CategoryMatch(string Slug, string? Keyword);

public interface ICategorizer
{
    CategoryMatch Categorize(string? description);
}

/// <summary>
/// Asigna la categoría con las reglas de palabras clave. Port de Categorizer en
/// tools/catalog-analysis/catalog.py: si algo difiere, el comportamiento correcto es el de catalog.py.
/// </summary>
public sealed class Categorizer : ICategorizer
{
    private static readonly CategoryMatch Others = new(CategoryRuleSet.OthersSlug, null);

    // Regla ya indexada. Es clase (no record) para compararla por referencia en el HashSet de candidatos.
    private sealed class Rule(string[] words, int priority, int position, string slug, string keyword)
    {
        public string[] Words { get; } = words;

        public int Priority { get; } = priority;

        /// <summary>Largo de la palabra clave sin espacios.</summary>
        public int Length { get; } = string.Concat(words).Length;

        public int Position { get; } = position;

        public string Slug { get; } = slug;

        public string Keyword { get; } = keyword;
    }

    private readonly Dictionary<string, List<Rule>> _index = new(StringComparer.Ordinal);

    public Categorizer(CategoryRuleSet rules)
    {
        CatalogText.EnsureUnicodeSupport();

        foreach (var category in rules.Categories)
        {
            foreach (var definition in category.Rules)
            {
                var words = CatalogText.Tokens(definition.Keyword).ToArray();
                var rule = new Rule(words, definition.Priority, category.Position, category.Slug, definition.Keyword);

                if (!_index.TryGetValue(words[0], out var list))
                {
                    list = [];
                    _index[words[0]] = list;
                }

                list.Add(rule);
            }
        }
    }

    public CategoryMatch Categorize(string? description)
    {
        var tokens = CatalogText.Tokens(description);
        var candidates = new HashSet<Rule>();

        foreach (var token in tokens)
        {
            AddCandidates(candidates, token);

            if (token.EndsWith('S'))
            {
                AddCandidates(candidates, token[..^1]);
            }

            if (token.EndsWith("ES", StringComparison.Ordinal))
            {
                AddCandidates(candidates, token[..^2]);
            }
        }

        Rule? best = null;
        foreach (var rule in candidates)
        {
            if (Matches(tokens, rule.Words) && (best is null || IsBetter(rule, best)))
            {
                best = rule;
            }
        }

        return best is null ? Others : new CategoryMatch(best.Slug, best.Keyword);
    }

    private void AddCandidates(HashSet<Rule> candidates, string key)
    {
        // catalog.py ignora la clave vacía (token "S" sin su última letra).
        if (key.Length > 0 && _index.TryGetValue(key, out var rules))
        {
            candidates.UnionWith(rules);
        }
    }

    /// <summary>Mayor prioridad; luego palabra clave más larga; luego menor posición de categoría.</summary>
    private static bool IsBetter(Rule a, Rule b)
    {
        if (a.Priority != b.Priority)
        {
            return a.Priority > b.Priority;
        }

        if (a.Length != b.Length)
        {
            return a.Length > b.Length;
        }

        if (a.Position != b.Position)
        {
            return a.Position < b.Position;
        }

        // catalog.py deja este empate al orden de un set de Python, que cambia entre ejecuciones
        // (la categoría es la misma, solo cambia cuál clave se reporta). Aquí se fija por orden alfabético.
        return string.CompareOrdinal(a.Keyword, b.Keyword) < 0;
    }

    private static bool Matches(List<string> tokens, string[] words)
    {
        for (var i = 0; i + words.Length <= tokens.Count; i++)
        {
            var all = true;
            for (var j = 0; j < words.Length; j++)
            {
                if (!SameWord(tokens[i + j], words[j]))
                {
                    all = false;
                    break;
                }
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>La palabra es igual a la clave, o la clave más S o ES.</summary>
    private static bool SameWord(string word, string keyword)
    {
        if (word.Length == keyword.Length)
        {
            return string.Equals(word, keyword, StringComparison.Ordinal);
        }

        if (!word.StartsWith(keyword, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = word.AsSpan(keyword.Length);
        return rest is "S" or "ES";
    }
}
