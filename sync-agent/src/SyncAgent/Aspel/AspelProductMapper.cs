using System.Data;
using System.Globalization;

namespace SyncAgent.Aspel;

/// <summary>Convierte una fila del resultado de <see cref="AspelQuery"/> en un <see cref="AspelProduct"/>.</summary>
public static class AspelProductMapper
{
    // Posición de cada columna en el SELECT de AspelQuery.
    private const int CodeOrdinal = 0;
    private const int DescriptionOrdinal = 1;
    private const int StockOrdinal = 2;
    private const int StatusOrdinal = 3;
    private const int PriceOrdinal = 4;

    public static AspelProduct Map(IDataRecord record) => new(
        // Sin Trim(): los espacios (al inicio o al final) forman parte del código.
        Code: record.GetString(CodeOrdinal),
        Description: ReadString(record, DescriptionOrdinal),
        Stock: ReadDouble(record, StockOrdinal),
        Status: ReadString(record, StatusOrdinal),
        Price: ReadDouble(record, PriceOrdinal));

    private static string? ReadString(IDataRecord record, int ordinal) =>
        record.IsDBNull(ordinal) ? null : record.GetString(ordinal);

    // Convert.ToDouble acepta float, real, decimal, money, int... según el tipo real de la columna
    // (se confirma en la tienda). No se redondea: PRECIO puede traer decimales de más (34.999999).
    private static double? ReadDouble(IDataRecord record, int ordinal) =>
        record.IsDBNull(ordinal)
            ? null
            : Convert.ToDouble(record.GetValue(ordinal), CultureInfo.InvariantCulture);
}
