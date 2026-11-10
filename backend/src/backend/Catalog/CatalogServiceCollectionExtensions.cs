namespace backend.Catalog;

public static class CatalogServiceCollectionExtensions
{
    /// <summary>
    /// Registra las reglas del catálogo (JSON embebido), el categorizador y el generador de nombres
    /// para mostrar. Son singletons sin estado mutable; la sincronización (US-06) los inyecta.
    /// </summary>
    public static IServiceCollection AddCatalogRules(this IServiceCollection services)
    {
        services.AddSingleton(_ => CategoryRuleSet.LoadEmbedded());
        services.AddSingleton(_ => DisplayDictionary.LoadEmbedded());
        services.AddSingleton<ICategorizer>(sp => new Categorizer(sp.GetRequiredService<CategoryRuleSet>()));
        services.AddSingleton<IDisplayNamer>(sp => new DisplayNamer(sp.GetRequiredService<DisplayDictionary>()));
        return services;
    }
}
