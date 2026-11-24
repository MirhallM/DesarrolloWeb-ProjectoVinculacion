using backend.Data.Entities;

namespace backend.Sync;

/// <summary>Valores que se guardan en <c>products</c> para un artículo recibido, o el motivo por el que se rechaza.</summary>
public sealed record SyncItemValues(
    string? RejectReason,
    string Description,
    decimal PublicPrice,
    decimal Stock,
    bool IsActive)
{
    public bool IsRejected => RejectReason is not null;
}

/// <summary>
/// Reglas de docs/business-rules.md que se aplican a cada artículo de Aspel. El agente envía los datos tal como
/// vienen; aquí se interpretan. Es una función pura para poder probarla sin base de datos.
/// </summary>
public static class SyncItemRules
{
    public const int MaxCodeLength = 50;          // products.code varchar(50)
    public const int MaxDescriptionLength = 255;  // products.description varchar(255)

    public static SyncItemValues Evaluate(string? code, string? description, double? stock, string? status, double? price)
    {
        // Un código "vacío" no identifica a ningún producto. El código NO se recorta: se valida tal cual.
        if (string.IsNullOrWhiteSpace(code))
        {
            return Rejected("código vacío");
        }

        if (code.Length > MaxCodeLength)
        {
            return Rejected($"código de más de {MaxCodeLength} caracteres");
        }

        var text = description ?? string.Empty; // se guarda como viene; null pasa a ""
        if (text.Length > MaxDescriptionLength)
        {
            return Rejected($"descripción de más de {MaxDescriptionLength} caracteres");
        }

        if (!TryConvert(price, ProductPrecision.MaxPrice, ProductPrecision.PriceScale, out var publicPrice))
        {
            return Rejected("precio fuera de rango");
        }

        // Sin precio en la lista 1 (o negativo): se guarda 0 y el producto no se muestra.
        if (publicPrice < 0m)
        {
            publicPrice = 0m;
        }

        if (!TryConvert(stock, ProductPrecision.MaxStock, ProductPrecision.StockScale, out var stockValue))
        {
            return Rejected("existencia fuera de rango");
        }

        // 'A' = activo; 'B' (baja) o cualquier otro valor: inactivo. Nunca se muestra algo que no sea explícitamente activo.
        var isActive = string.Equals(status?.Trim(), "A", StringComparison.OrdinalIgnoreCase);

        return new SyncItemValues(null, text, publicPrice, stockValue, isActive);
    }

    /// <summary>null pasa a 0; se redondea (el float de SQL Server trae decimales de más: 34.999999 → 35.00).</summary>
    private static bool TryConvert(double? value, decimal max, int scale, out decimal result)
    {
        result = 0m;
        if (value is null)
        {
            return true;
        }

        var v = value.Value;
        if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > (double)max)
        {
            return false;
        }

        result = Math.Round((decimal)v, scale, MidpointRounding.AwayFromZero);
        return Math.Abs(result) <= max;
    }

    private static SyncItemValues Rejected(string reason) => new(reason, string.Empty, 0m, 0m, false);
}
