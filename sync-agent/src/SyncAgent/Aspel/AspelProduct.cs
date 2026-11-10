namespace SyncAgent.Aspel;

/// <summary>
/// Un artículo de Aspel SAE tal como viene de la base. El agente NO transforma estos datos:
/// el redondeo del precio, el nombre para mostrar, la categoría y la visibilidad los aplica la API (US-06).
/// </summary>
/// <param name="Code"><c>INVE01.CVE_ART</c>. Se conserva exacto: no se recorta ni se normaliza
/// (hay códigos que difieren solo por espacios al inicio y son productos distintos).</param>
/// <param name="Description"><c>INVE01.DESCR</c>. Puede ser nula, vacía o el texto literal "NULL".</param>
/// <param name="Stock"><c>INVE01.EXIST</c>. Puede tener decimales, ser negativa o estar inflada.</param>
/// <param name="Status"><c>INVE01.STATUS</c>: 'A' = activo, 'B' = baja.</param>
/// <param name="Price"><c>PRECIO_X_PROD01.PRECIO</c> de la lista configurada. Nulo si el artículo
/// no tiene precio en esa lista (LEFT JOIN).</param>
public sealed record AspelProduct(
    string Code,
    string? Description,
    double? Stock,
    string? Status,
    double? Price);
