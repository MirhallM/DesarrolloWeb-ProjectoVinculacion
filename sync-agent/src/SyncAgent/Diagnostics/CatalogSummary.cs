using SyncAgent.Aspel;

namespace SyncAgent.Diagnostics;

/// <summary>Conteos del inventario leído. Solo informativos: el agente no filtra ni modifica nada.</summary>
public sealed record CatalogSummary
{
    public int TotalRows { get; init; }
    /// <summary>Códigos distintos (comparados tal cual, con espacios). Si es menor que <see cref="TotalRows"/>, el JOIN está duplicando artículos.</summary>
    public int DistinctCodes { get; init; }

    public int Active { get; init; }
    public int Inactive { get; init; }
    public int OtherStatus { get; init; }
    /// <summary>Hasta 5 valores distintos de STATUS que no son 'A' ni 'B' (ayuda a diagnosticar).</summary>
    public IReadOnlyList<string> OtherStatusSamples { get; init; } = [];

    public int ActiveWithoutPrice { get; init; }
    public int ActiveWithZeroPrice { get; init; }
    public int ActiveWithSuspiciousPrice { get; init; }

    public int DescriptionNull { get; init; }
    public int DescriptionEmpty { get; init; }
    public int DescriptionLiteralNull { get; init; }
    public int ActiveWithoutUsableDescription { get; init; }

    public int CodesWithLeadingSpaces { get; init; }
    public int CodesWithTrailingSpaces { get; init; }
    /// <summary>Grupos de códigos distintos que serían iguales al recortar espacios (p. ej. "  205" y "205").</summary>
    public int CodeGroupsDifferingOnlyBySpaces { get; init; }

    public int StockNegative { get; init; }
    public int StockWithDecimals { get; init; }
    public int StockOver10Thousand { get; init; }
    public int StockOver1Million { get; init; }

    public int MaxCodeLength { get; init; }
    public int MaxDescriptionLength { get; init; }
}

/// <summary>Acumula un <see cref="CatalogSummary"/> artículo por artículo.</summary>
public sealed class CatalogSummaryBuilder
{
    /// <summary>Precio a partir del cual se marca como sospechoso (regla pendiente de validar, docs/business-rules.md).</summary>
    public const double SuspiciousPriceThreshold = 100_000;

    private readonly HashSet<string> _rawCodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _rawCodesPerTrimmedCode = new(StringComparer.Ordinal);
    private readonly List<string> _otherStatusSamples = [];

    private int _total, _active, _inactive, _otherStatus;
    private int _activeNoPrice, _activeZeroPrice, _activeSuspiciousPrice;
    private int _descNull, _descEmpty, _descLiteralNull, _activeBadDescription;
    private int _leading, _trailing;
    private int _stockNegative, _stockDecimals, _stockOver10k, _stockOver1m;
    private int _maxCode, _maxDescription;

    public void Add(AspelProduct product)
    {
        _total++;

        var code = product.Code;
        // Aquí SÍ se recorta, pero solo para medir cuántos códigos chocarían; el código nunca se modifica.
        if (_rawCodes.Add(code))
        {
            var trimmed = code.Trim();
            _rawCodesPerTrimmedCode[trimmed] = _rawCodesPerTrimmedCode.GetValueOrDefault(trimmed) + 1;
        }

        if (code.Length > 0 && char.IsWhiteSpace(code[0])) _leading++;
        if (code.Length > 0 && char.IsWhiteSpace(code[^1])) _trailing++;
        _maxCode = Math.Max(_maxCode, code.Length);

        var status = product.Status?.Trim();
        var isActive = string.Equals(status, "A", StringComparison.OrdinalIgnoreCase);
        if (isActive) _active++;
        else if (string.Equals(status, "B", StringComparison.OrdinalIgnoreCase)) _inactive++;
        else
        {
            _otherStatus++;
            var sample = product.Status ?? "(NULL)";
            if (_otherStatusSamples.Count < 5 && !_otherStatusSamples.Contains(sample)) _otherStatusSamples.Add(sample);
        }

        var badDescription = false;
        if (product.Description is null) { _descNull++; badDescription = true; }
        else if (string.IsNullOrWhiteSpace(product.Description)) { _descEmpty++; badDescription = true; }
        else if (string.Equals(product.Description.Trim(), "NULL", StringComparison.OrdinalIgnoreCase)) { _descLiteralNull++; badDescription = true; }
        _maxDescription = Math.Max(_maxDescription, product.Description?.Length ?? 0);

        if (isActive)
        {
            if (badDescription) _activeBadDescription++;
            if (product.Price is null) _activeNoPrice++;
            else if (product.Price == 0) _activeZeroPrice++;
            else if (product.Price >= SuspiciousPriceThreshold) _activeSuspiciousPrice++;
        }

        if (product.Stock is { } stock && double.IsFinite(stock))
        {
            if (stock < 0) _stockNegative++;
            if (stock != Math.Floor(stock)) _stockDecimals++;
            if (stock >= 10_000) _stockOver10k++;
            if (stock >= 1_000_000) _stockOver1m++;
        }
    }

    public CatalogSummary Build() => new()
    {
        TotalRows = _total,
        DistinctCodes = _rawCodes.Count,
        Active = _active,
        Inactive = _inactive,
        OtherStatus = _otherStatus,
        OtherStatusSamples = _otherStatusSamples.ToArray(),
        ActiveWithoutPrice = _activeNoPrice,
        ActiveWithZeroPrice = _activeZeroPrice,
        ActiveWithSuspiciousPrice = _activeSuspiciousPrice,
        DescriptionNull = _descNull,
        DescriptionEmpty = _descEmpty,
        DescriptionLiteralNull = _descLiteralNull,
        ActiveWithoutUsableDescription = _activeBadDescription,
        CodesWithLeadingSpaces = _leading,
        CodesWithTrailingSpaces = _trailing,
        CodeGroupsDifferingOnlyBySpaces = _rawCodesPerTrimmedCode.Count(kv => kv.Value > 1),
        StockNegative = _stockNegative,
        StockWithDecimals = _stockDecimals,
        StockOver10Thousand = _stockOver10k,
        StockOver1Million = _stockOver1m,
        MaxCodeLength = _maxCode,
        MaxDescriptionLength = _maxDescription,
    };

    public static CatalogSummary From(IEnumerable<AspelProduct> products)
    {
        var builder = new CatalogSummaryBuilder();
        foreach (var product in products) builder.Add(product);
        return builder.Build();
    }
}
