namespace backend.Data.Entities;

/// <summary>Precisión de las columnas numéricas de <see cref="Product"/> y sus valores máximos.</summary>
public static class ProductPrecision
{
    public const int PriceDigits = 18;
    public const int PriceScale = 2;
    public const int StockDigits = 20;
    public const int StockScale = 4;

    /// <summary>Mayor valor absoluto que cabe en <c>numeric(18,2)</c>: 16 dígitos enteros.</summary>
    public const decimal MaxPrice = 9_999_999_999_999_999.99m;

    /// <summary>Mayor valor absoluto que cabe en <c>numeric(20,4)</c>: 16 dígitos enteros.</summary>
    public const decimal MaxStock = 9_999_999_999_999_999.9999m;
}
