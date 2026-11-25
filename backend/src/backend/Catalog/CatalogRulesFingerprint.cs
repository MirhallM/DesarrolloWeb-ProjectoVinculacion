using System.Security.Cryptography;
using System.Text;

namespace backend.Catalog;

/// <summary>
/// Huella (SHA-256 en hexadecimal) de todo lo que decide la categoría y el nombre para mostrar de un producto:
/// <c>categories.json</c>, <c>display-dictionary.json</c>, <see cref="AlgorithmVersion"/> y, si existe, el commit
/// desplegado en Railway (<c>RAILWAY_GIT_COMMIT_SHA</c>).
/// </summary>
/// <remarks>
/// La sincronización guarda la huella en <c>sync_runs.rules_fingerprint</c>. Una corrida "sin cambios" solo se acepta si
/// la última corrida completa se aplicó con la huella actual. Así, después de cada despliegue, y también si un contenedor
/// viejo completó una corrida con las reglas anteriores (deja la columna en NULL), la siguiente corrida es completa y
/// deja los productos con las reglas vigentes. Ver docs/architecture.md, sección 5.1.
/// </remarks>
public sealed class CatalogRulesFingerprint(string value)
{
    /// <summary>
    /// Súbala a mano si cambia el código de <see cref="Categorizer"/>, <see cref="DisplayNamer"/> o
    /// <see cref="CatalogText"/> sin que cambie ningún JSON. En Railway no hace falta: el commit ya cambia la huella.
    /// </summary>
    public const string AlgorithmVersion = "1";

    /// <summary>Variable que Railway define en cada despliegue; en local y en el CI no existe.</summary>
    public const string CommitVariable = "RAILWAY_GIT_COMMIT_SHA";

    public string Value { get; } = value;

    /// <summary>Huella con los JSON embebidos y el commit del entorno (si lo hay).</summary>
    public static CatalogRulesFingerprint FromEnvironment() => new(Compute(
        CategoryRuleSet.ReadEmbeddedText(CategoryRuleSet.EmbeddedCategoriesName),
        CategoryRuleSet.ReadEmbeddedText(DisplayDictionary.EmbeddedName),
        AlgorithmVersion,
        Environment.GetEnvironmentVariable(CommitVariable)));

    /// <summary>Función pura: las mismas entradas dan la misma huella. Sin commit (null o vacío) es igual que no pasarlo.</summary>
    public static string Compute(string categoriesJson, string dictionaryJson, string algorithmVersion, string? commitSha)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, categoriesJson);
        Append(hash, dictionaryJson);
        Append(hash, algorithmVersion);
        Append(hash, string.IsNullOrWhiteSpace(commitSha) ? string.Empty : commitSha.Trim());
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // Con el largo por delante, "ab" + "c" y "a" + "bc" no dan la misma huella.
    private static void Append(IncrementalHash hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
