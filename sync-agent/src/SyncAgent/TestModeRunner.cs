using System.Diagnostics;
using System.Globalization;
using System.Text;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;

namespace SyncAgent;

/// <summary>
/// Modo de prueba (<c>--test</c>): una sola lectura, un informe en pantalla y nada más.
/// No envía datos a la API ni escribe el estado en disco. Sirve para validar la consulta contra la base real.
/// </summary>
public sealed class TestModeRunner(
    AspelProductLoader loader,
    AspelOptions options,
    string connectionDescription,
    TextWriter output)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        output.WriteLine("=== SyncAgent: modo de prueba (solo lectura, no se envía nada) ===");
        output.WriteLine($"Conexión: {connectionDescription}");
        output.WriteLine($"Lista de precios (CVE_PRECIO): {options.PriceList}");
        output.WriteLine("Leyendo...");

        var total = Stopwatch.StartNew();
        AspelLoadResult result;
        try
        {
            result = await loader.LoadAsync(cancellationToken);
        }
        catch (AspelReadException ex)
        {
            output.WriteLine();
            output.WriteLine($"NO SE PUDO LEER: {ex.Message}");
            return ExitCodes.SqlFailure;
        }

        var summary = CatalogSummaryBuilder.From(result.Products);
        total.Stop();

        output.Write(FormatReport(summary, result, total.Elapsed, ProcessMemory.Capture()));
        return ExitCodes.Success;
    }

    /// <summary>Arma el informe. Es una función pura para poder probarla.</summary>
    public static string FormatReport(CatalogSummary s, AspelLoadResult result, TimeSpan totalDuration, ProcessMemory memory)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        void Line(string text = "") => sb.AppendLine(text);
        void Row(string label, object value, string? note = null) =>
            Line($"  {label,-46} {string.Format(c, "{0:N0}", value),10}{(note is null ? "" : "   " + note)}");

        Line();
        Line("--- Artículos ---");
        Row("Total de filas leídas", s.TotalRows);
        Row("Códigos distintos", s.DistinctCodes,
            s.DistinctCodes == s.TotalRows ? "(igual al total: el JOIN no duplica)" : "<< MENOR QUE EL TOTAL: hay artículos repetidos");
        Row("Activos (STATUS = A)", s.Active);
        Row("De baja (STATUS = B)", s.Inactive);
        Row("Otro estado", s.OtherStatus,
            s.OtherStatusSamples.Count > 0 ? "valores: " + string.Join(", ", s.OtherStatusSamples.Select(v => $"'{v}'")) : null);

        Line();
        Line("--- Precios (solo activos) ---");
        Row("Activos sin precio en la lista", s.ActiveWithoutPrice);
        Row("Activos con precio 0", s.ActiveWithZeroPrice);
        Row($"Activos con precio >= {CatalogSummaryBuilder.SuspiciousPriceThreshold:N0}", s.ActiveWithSuspiciousPrice, "(sospechoso)");

        Line();
        Line("--- Descripciones ---");
        Row("Nulas (NULL de la base)", s.DescriptionNull);
        Row("Vacías o solo espacios", s.DescriptionEmpty);
        Row("Con el texto literal \"NULL\"", s.DescriptionLiteralNull);
        Row("Activos sin descripción utilizable", s.ActiveWithoutUsableDescription);
        Row("Longitud máxima de DESCR", s.MaxDescriptionLength, "(esperado: 40)");

        Line();
        Line("--- Códigos (CVE_ART) ---");
        Row("Con espacios al inicio", s.CodesWithLeadingSpaces);
        Row("Con espacios al final", s.CodesWithTrailingSpaces,
            s.CodesWithTrailingSpaces > 0 ? "(la columna podría ser CHAR con relleno)" : null);
        Row("Grupos que solo difieren por espacios", s.CodeGroupsDifferingOnlyBySpaces, "(productos distintos; no se recortan)");
        Row("Longitud máxima de CVE_ART", s.MaxCodeLength, "(esperado: 16)");

        Line();
        Line("--- Existencias ---");
        Row("Negativas", s.StockNegative);
        Row("Con decimales", s.StockWithDecimals);
        Row("De 10 000 o más", s.StockOver10Thousand);
        Row("De 1 000 000 o más", s.StockOver1Million, "(valores de relleno)");

        Line();
        Line("--- Tiempos ---");
        Line($"  Conexión:            {result.Metrics.ConnectDuration.TotalMilliseconds,10:N0} ms");
        Line($"  Consulta + lectura:  {result.Metrics.QueryDuration.TotalMilliseconds,10:N0} ms");
        Line($"  Total (con resumen): {totalDuration.TotalMilliseconds,10:N0} ms");
        Line($"  Intentos:            {result.Attempts,10}");

        Line();
        Line("--- Memoria del proceso ---");
        Line($"  Máximo en memoria física (working set): {ProcessMemory.ToMegabytes(memory.PeakWorkingSetBytes),8:N1} MB");
        Line($"  Máximo de memoria virtual comprometida: {ProcessMemory.ToMegabytes(memory.PeakPagedBytes),8:N1} MB");
        Line($"  Heap administrado actual:               {ProcessMemory.ToMegabytes(memory.ManagedHeapBytes),8:N1} MB");
        Line($"  Total asignado durante la ejecución:    {ProcessMemory.ToMegabytes(memory.TotalAllocatedBytes),8:N1} MB");
        Line($"  Memoria disponible según el GC:         {ProcessMemory.ToMegabytes(memory.GcAvailableBytes),8:N1} MB   (con el tope activo debe ser 64)");
        Line($"  Recolecciones del GC (gen0 / gen2):     {memory.Gen0Collections} / {memory.Gen2Collections}");

        Line();
        Line("Lectura terminada. No se envió ni se guardó nada.");
        return sb.ToString();
    }
}
