namespace SyncAgent.Aspel;

/// <summary>
/// La única consulta que ejecuta el agente contra Aspel SAE. Es un SELECT de solo lectura.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Los nombres de tablas están fijos en el texto (no son configurables) para evitar inyección SQL.
/// La lista de precios viaja como parámetro.</item>
/// <item>LEFT JOIN: los artículos sin precio en la lista no se pierden; llegan con <c>PRECIO</c> nulo.</item>
/// <item>Sin <c>ORDER BY</c>: ordenar obliga a SQL Server a gastar memoria y la API no lo necesita.</item>
/// <item>Sin <c>NOLOCK</c>: puede devolver filas duplicadas u omitidas; se usa el aislamiento por defecto.</item>
/// </list>
/// El orden de las columnas debe coincidir con las constantes de <see cref="AspelProductMapper"/>.
/// </remarks>
public static class AspelQuery
{
    public const string PriceListParameter = "@priceList";

    public const string Sql =
        """
        SELECT i.CVE_ART, i.DESCR, i.EXIST, i.STATUS, p.PRECIO
        FROM dbo.INVE01 AS i
        LEFT JOIN dbo.PRECIO_X_PROD01 AS p
            ON p.CVE_ART = i.CVE_ART
           AND p.CVE_PRECIO = @priceList;
        """;
}
