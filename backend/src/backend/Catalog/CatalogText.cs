using System.Globalization;
using System.Text;

namespace backend.Catalog;

/// <summary>
/// Normalización y separación en palabras. Equivale a normalize() y tokens() de
/// tools/catalog-analysis/catalog.py: mayúsculas, sin acentos (Ñ → N) y separado
/// por todo lo que no sea A–Z o 0–9.
/// </summary>
public static class CatalogText
{
    // Python usa str.upper() (mayúsculas completas: ß → SS) y aquí ToUpperInvariant()
    // (mapeo simple: ß queda igual). El inventario de Aspel solo trae caracteres Latin-1,
    // donde ambos coinciden; la prueba de paridad vigila que siga siendo así.

    /// <summary>Mayúsculas y sin acentos: NFD y se descartan las marcas combinantes (Mn).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>Palabras de la descripción normalizada, separadas por todo lo que no sea A–Z o 0–9.</summary>
    public static List<string> Tokens(string? text)
    {
        var normalized = Normalize(text);
        var tokens = new List<string>();
        var start = -1;

        for (var i = 0; i < normalized.Length; i++)
        {
            if (IsTokenChar(normalized[i]))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                tokens.Add(normalized[start..i]);
                start = -1;
            }
        }

        if (start >= 0)
        {
            tokens.Add(normalized[start..]);
        }

        return tokens;
    }

    /// <summary>
    /// Falla con un mensaje claro si el entorno no puede quitar acentos (por ejemplo, un contenedor
    /// sin ICU o con InvariantGlobalization): sin esto las categorías saldrían mal sin avisar.
    /// </summary>
    public static void EnsureUnicodeSupport()
    {
        if (Normalize("áñü") != "ANU")
        {
            throw new InvalidOperationException(
                "Este entorno no normaliza Unicode (NFD) y no se pueden quitar los acentos de las descripciones. " +
                "Verifica que el runtime tenga ICU y que InvariantGlobalization no esté activado.");
        }
    }

    private static bool IsTokenChar(char c) => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9');
}
