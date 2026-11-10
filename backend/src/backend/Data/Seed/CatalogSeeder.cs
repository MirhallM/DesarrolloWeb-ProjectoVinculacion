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
    IReadOnlyList<string> Warnings)
{
    public override string ToString() =>
        $"Categorías: +{CategoriesAdded} ~{CategoriesUpdated} -{CategoriesRemoved}. " +
        $"Reglas: +{RulesAdded} ~{RulesUpdated} -{RulesRemoved}. " +
        $"Advertencias: {Warnings.Count}.";
}

/// <summary>
/// Deja las tablas categories y category_rules iguales a categories.json. Se puede ejecutar
/// varias veces sin duplicar nada, y lo que se quita del JSON desaparece de las tablas.
/// </summary>
/// <remarks>
/// Una categoría quitada del JSON que todavía tiene productos NO se borra: queda sin reglas y
/// se avisa en el reporte y en el log. La categoría "otros" nunca se borra.
/// </remarks>
public sealed class CatalogSeeder(AppDbContext db, ILogger<CatalogSeeder> logger)
{
    public async Task<CatalogSeedReport> SeedAsync(CategoryRuleSet rules, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        int categoriesAdded = 0, categoriesUpdated = 0, categoriesRemoved = 0;
        int rulesAdded = 0, rulesUpdated = 0, rulesRemoved = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

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

        foreach (var category in categories.Where(c => !wantedSlugs.Contains(c.Slug)).ToList())
        {
            if (category.Id == Category.OthersId)
            {
                continue;
            }

            if (await db.Products.AnyAsync(p => p.CategoryId == category.Id, ct))
            {
                var warning = $"La categoría '{category.Slug}' ya no está en categories.json pero tiene productos asignados; no se borró.";
                warnings.Add(warning);
                logger.LogWarning("{Warning}", warning);
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
        var bySlug = categories.ToDictionary(c => c.Slug, StringComparer.Ordinal);
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
        await transaction.CommitAsync(ct);

        var report = new CatalogSeedReport(
            categoriesAdded, categoriesUpdated, categoriesRemoved,
            rulesAdded, rulesUpdated, rulesRemoved, warnings);
        logger.LogInformation("Siembra del catálogo: {Report}", report);
        return report;
    }
}
