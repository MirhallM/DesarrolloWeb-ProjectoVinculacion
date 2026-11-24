using backend.Data.Entities;

namespace backend.Catalog;

/// <summary>
/// Campos que la aplicación deriva de la descripción de Aspel: nombre para mostrar y categoría
/// (docs/business-rules.md, secciones 4 y 5). Lo usan la sincronización y la siembra de desarrollo.
/// </summary>
public sealed class CatalogProductFields(ICategorizer categorizer, IDisplayNamer displayNamer)
{
    /// <summary>Descripción con texto real: no nula, no vacía y distinta del texto literal "NULL".</summary>
    public static bool HasUsableDescription(string? description) =>
        !string.IsNullOrWhiteSpace(description)
        && !description.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nombre para mostrar; vacío si la descripción no es utilizable (el producto no se muestra).</summary>
    public string GetDisplayName(string? description) =>
        HasUsableDescription(description) ? displayNamer.GetDisplayName(description) : string.Empty;

    /// <summary>Id de la categoría; si el slug no existe en la base, la categoría "Otros".</summary>
    public Guid GetCategoryId(string? description, IReadOnlyDictionary<string, Guid> categoryIdsBySlug) =>
        categoryIdsBySlug.TryGetValue(categorizer.Categorize(description).Slug, out var id) ? id : Category.OthersId;
}
