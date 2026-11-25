using backend.Catalog;
using backend.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Seed;

/// <summary>Resumen de lo que hizo la siembra.</summary>
public sealed record CatalogSeedReport(
    int CategoriesAdded,
    int CategoriesUpdated,
    int CategoriesRemoved,
    int RulesAdded,
    int RulesUpdated,
    int RulesRemoved,
    IReadOnlyList<string> Warnings,
    int ProductsCategoryChanged = 0,
    int ProductsDisplayNameChanged = 0)
{
    public override string ToString() =>
        $"Categorías: +{CategoriesAdded} ~{CategoriesUpdated} -{CategoriesRemoved}. " +
        $"Reglas: +{RulesAdded} ~{RulesUpdated} -{RulesRemoved}. " +
        $"Productos: {ProductsCategoryChanged} con otra categoría, {ProductsDisplayNameChanged} con otro nombre. " +
        $"Advertencias: {Warnings.Count}.";
}

/// <summary>
/// Deja las tablas categories y category_rules iguales a categories.json y recalcula la categoría y el nombre para
/// mostrar de todos los productos con las reglas y el diccionario actuales. Se puede ejecutar varias veces sin duplicar
/// nada, y lo que se quita del JSON desaparece de las tablas.
/// </summary>
/// <remarks>
/// Todo ocurre en una sola transacción. Primero toma un bloqueo que impide escribir en products (las lecturas del
/// catálogo público siguen funcionando): si una sincronización está completándose, la siembra espera a que termine, y
/// una que empiece después espera a que la siembra termine. La recategorización solo escribe las filas que cambian
/// (así que solo esas cambian <c>updated_at</c>) y no toca <c>synced_at</c>: no es una sincronización.
/// Una categoría quitada del JSON no se borra mientras tenga productos: se aparta con un nombre temporal, sus productos
/// pasan a la categoría que indiquen las reglas y entonces se borra. La categoría "otros" nunca se borra.
/// </remarks>
public sealed class CatalogSeeder(AppDbContext db, ILogger<CatalogSeeder> logger)
{
    /// <summary>Marca del nombre temporal de una categoría quitada que todavía tiene productos.</summary>
    private const string RetiredNamePrefix = "~retirada~";

    /// <summary>Cuánto espera la siembra a una sincronización en curso antes de fallar (y el pre-deploy no avanza).</summary>
    private const string LockTimeoutSql = "SET LOCAL lock_timeout = '60s'";

    public async Task<CatalogSeedReport> SeedAsync(CategoryRuleSet rules, DisplayDictionary dictionary, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        int categoriesAdded = 0, categoriesUpdated = 0, categoriesRemoved = 0;
        int rulesAdded = 0, rulesUpdated = 0, rulesRemoved = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Mientras dure la transacción nadie más escribe en products.
        await db.Database.ExecuteSqlRawAsync(LockTimeoutSql, ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE products IN EXCLUSIVE MODE", ct);

        var categories = await db.Categories.ToListAsync(ct);
        var dbRules = await db.CategoryRules.ToListAsync(ct);

        var wantedSlugs = rules.Categories.Select(c => c.Slug).ToHashSet(StringComparer.Ordinal);
        var wantedRules = rules.Categories
            .SelectMany(c => c.Rules.Select(r => (Rule: r, Slug: c.Slug)))
            .ToDictionary(x => x.Rule.Keyword, StringComparer.Ordinal);

        // 1. Primero se quita lo que sobra, para que un nombre o una clave reutilizados no choquen con el índice único.
        foreach (var rule in dbRules.Where(r => !wantedRules.ContainsKey(r.Keyword)).ToList())
        {
            db.CategoryRules.Remove(rule);
            dbRules.Remove(rule);
            rulesRemoved++;
        }

        // Categorías quitadas del JSON: las vacías se borran ya; las que tienen productos se apartan con un nombre
        // temporal (el nombre original podría ser reutilizado por una categoría nueva) y se borran después de mover
        // sus productos.
        var retired = new List<(Category Category, string OriginalName)>();
        foreach (var category in categories.Where(c => !wantedSlugs.Contains(c.Slug)).ToList())
        {
            if (category.Id == Category.OthersId)
            {
                continue;
            }

            if (await db.Products.AnyAsync(p => p.CategoryId == category.Id, ct))
            {
                retired.Add((category, category.Name));
                category.Name = RetiredNamePrefix + category.Id.ToString("N");
                continue;
            }

            // Sus reglas ya se quitaron arriba (ninguna de sus claves puede seguir en el JSON bajo otra categoría
            // sin haberse movido); el cascade cubre cualquier resto.
            db.Categories.Remove(category);
            categories.Remove(category);
            categoriesRemoved++;
        }

        await db.SaveChangesAsync(ct);

        // 2. Categorías: alta o actualización por slug.
        var bySlug = categories.Where(c => wantedSlugs.Contains(c.Slug)).ToDictionary(c => c.Slug, StringComparer.Ordinal);
        foreach (var definition in rules.Categories)
        {
            if (bySlug.TryGetValue(definition.Slug, out var existing))
            {
                if (existing.Name != definition.Name || existing.Position != definition.Position)
                {
                    existing.Name = definition.Name;
                    existing.Position = definition.Position;
                    categoriesUpdated++;
                }
            }
            else
            {
                var created = new Category
                {
                    Id = Guid.NewGuid(),
                    Slug = definition.Slug,
                    Name = definition.Name,
                    Position = definition.Position
                };
                db.Categories.Add(created);
                bySlug[definition.Slug] = created;
                categoriesAdded++;
            }
        }

        await db.SaveChangesAsync(ct);

        // 3. Reglas: alta o actualización por palabra clave (una clave pertenece a una sola categoría).
        var byKeyword = dbRules.ToDictionary(r => r.Keyword, StringComparer.Ordinal);
        foreach (var (rule, slug) in wantedRules.Values)
        {
            var categoryId = bySlug[slug].Id;

            if (byKeyword.TryGetValue(rule.Keyword, out var existing))
            {
                if (existing.CategoryId != categoryId || existing.Priority != rule.Priority)
                {
                    existing.CategoryId = categoryId;
                    existing.Priority = rule.Priority;
                    rulesUpdated++;
                }
            }
            else
            {
                db.CategoryRules.Add(new CategoryRule
                {
                    Id = Guid.NewGuid(),
                    CategoryId = categoryId,
                    Keyword = rule.Keyword,
                    Priority = rule.Priority
                });
                rulesAdded++;
            }
        }

        await db.SaveChangesAsync(ct);

        // 4. Recategorizar y recalcular el nombre de todo el catálogo.
        var (categoryChanged, displayNameChanged) = await RecalculateProductsAsync(rules, dictionary, bySlug, ct);

        // 5. Las categorías retiradas ya no deberían tener productos: se borran. Si alguna aún los tuviera (no debería
        // pasar), se le devuelve el nombre y se avisa.
        foreach (var (category, originalName) in retired)
        {
            if (await db.Products.AnyAsync(p => p.CategoryId == category.Id, ct))
            {
                category.Name = originalName;
                var warning = $"La categoría '{category.Slug}' ya no está en categories.json pero tiene productos asignados; no se borró.";
                warnings.Add(warning);
                logger.LogWarning("{Warning}", warning);
                continue;
            }

            db.Categories.Remove(category);
            categoriesRemoved++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var report = new CatalogSeedReport(
            categoriesAdded, categoriesUpdated, categoriesRemoved,
            rulesAdded, rulesUpdated, rulesRemoved, warnings,
            categoryChanged, displayNameChanged);
        logger.LogInformation("Siembra del catálogo: {Report}", report);
        return report;
    }

    /// <summary>
    /// Calcula categoría y nombre de cada producto con las reglas dadas y guarda solo los que difieren. No toca
    /// <c>synced_at</c>; <c>updated_at</c> cambia únicamente en las filas modificadas.
    /// </summary>
    private async Task<(int CategoryChanged, int DisplayNameChanged)> RecalculateProductsAsync(
        CategoryRuleSet rules,
        DisplayDictionary dictionary,
        IReadOnlyDictionary<string, Category> categoriesBySlug,
        CancellationToken ct)
    {
        var fields = new CatalogProductFields(new Categorizer(rules), new DisplayNamer(dictionary));
        var categoryIds = categoriesBySlug.ToDictionary(c => c.Key, c => c.Value.Id, StringComparer.Ordinal);

        int categoryChanged = 0, displayNameChanged = 0;

        var products = await db.Products.ToListAsync(ct);
        foreach (var product in products)
        {
            var categoryId = fields.GetCategoryId(product.Description, categoryIds);
            if (product.CategoryId != categoryId)
            {
                product.CategoryId = categoryId;
                categoryChanged++;
            }

            var displayName = fields.GetDisplayName(product.Description);
            if (!string.Equals(product.DisplayName, displayName, StringComparison.Ordinal))
            {
                product.DisplayName = displayName;
                displayNameChanged++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (categoryChanged, displayNameChanged);
    }
}
