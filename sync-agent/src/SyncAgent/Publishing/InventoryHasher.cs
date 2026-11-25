using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SyncAgent.Aspel;

namespace SyncAgent.Publishing;

/// <summary>
/// Huella (SHA-256) del inventario completo. La API la compara con la de la última corrida aplicada: si coincide,
/// registra "sin cambios" y el agente no envía los productos. No depende del orden de lectura.
/// </summary>
public static class InventoryHasher
{
    /// <summary>Si cambia el formato de la línea, se cambia la versión: así una huella vieja nunca coincide.</summary>
    private const string Version = "nettes-inventory-v1";

    private const char Separator = '\u001F'; // separador de unidad: no aparece en datos de Aspel

    public static string Compute(IReadOnlyList<AspelProduct> products)
    {
        // Se ordena una lista de índices (no se copian los artículos): orden ordinal exacto del código.
        var order = new int[products.Count];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) => string.CompareOrdinal(products[a].Code, products[b].Code));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var line = new StringBuilder(128);
        Append(hash, line.Append(Version).Append('\n'));

        foreach (var index in order)
        {
            var p = products[index];
            line.Clear();
            Text(line, p.Code).Append(Separator);
            Text(line, p.Description).Append(Separator);
            Number(line, p.Stock).Append(Separator);
            Text(line, p.Status).Append(Separator);
            Number(line, p.Price).Append('\n');
            Append(hash, line);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // "N" para nulo y "S"/"D" como prefijo de un valor: un texto "N" no se confunde con un nulo.
    private static StringBuilder Text(StringBuilder sb, string? value) =>
        value is null ? sb.Append('N') : sb.Append('S').Append(value);

    private static StringBuilder Number(StringBuilder sb, double? value) =>
        value is null ? sb.Append('N') : sb.Append('D').Append(value.Value.ToString("R", CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash hash, StringBuilder line) =>
        hash.AppendData(Encoding.UTF8.GetBytes(line.ToString()));
}
