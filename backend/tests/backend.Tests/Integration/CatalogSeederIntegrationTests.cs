using backend.Catalog;
using backend.Data;
using backend.Data.Entities;
using backend.Data.Seed;
using backend.Tests.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace backend.Tests.Integration;

/// <summary>Siembra de categorías y reglas contra un PostgreSQL real (base temporal con las migraciones).</summary>
[Trait("Category", "Integration")]
public sealed class CatalogSeederIntegrationTests : IAsyncLifetime
{
    private static readonly CategoryRuleSet Full = CategoryRuleSet.LoadEmbedded();

    private TempDatabase? _database;

    public async ValueTask InitializeAsync()
    {
        if (IntegrationEnvironment.IsConfigured)
        {
            _database = await TempDatabase.CreateAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private static int TotalRules(CategoryRuleSet rules) => rules.Categories.Sum(c => c.Rules.Count);

    private async Task<CatalogSeedReport> SeedAsync(CategoryRuleSet rules)
    {
        await using var db = _database!.CreateContext();
        return await new CatalogSeeder(db, NullLogger<CatalogSeeder>.Instance)
            .SeedAsync(rules, TestContext.Current.CancellationToken);
    }

    [PostgresFact]
    public async Task Sembrar_deja_las_tablas_iguales_al_json()
    {
        var report = await SeedAsync(Full);

        await using var db = _database!.CreateContext();
        Assert.Equal(Full.Categories.Count, await db.Categories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TotalRules(Full), await db.CategoryRules.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Full.Categories.Count - 1, report.CategoriesAdded); // "otros" ya viene de la migración
        Assert.Equal(TotalRules(Full), report.RulesAdded);
        Assert.Empty(report.Warnings);
    }

    [PostgresFact]
    public async Task Sembrar_varias_veces_no_duplica_ni_cambia_nada()
    {
        await SeedAsync(Full);
        var second = await SeedAsync(Full);
        var third = await SeedAsync(Full);

        Assert.Equal(new CatalogSeedReport(0, 0, 0, 0, 0, 0, []), second with { Warnings = [] });
        Assert.Equal(new CatalogSeedReport(0, 0, 0, 0, 0, 0, []), third with { Warnings = [] });

        await using var db = _database!.CreateContext();
        Assert.Equal(Full.Categories.Count, await db.Categories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TotalRules(Full), await db.CategoryRules.CountAsync(TestContext.Current.CancellationToken));
    }

    [PostgresFact]
    public async Task Otros_conserva_su_id_fijo()
    {
        await SeedAsync(Full);

        await using var db = _database!.CreateContext();
        var others = await db.Categories.SingleAsync(c => c.Slug == "otros", TestContext.Current.CancellationToken);
        Assert.Equal(Category.OthersId, others.Id);
        Assert.Equal(1000, others.Position);
    }

    [PostgresFact]
    public async Task Una_regla_quitada_del_json_desaparece_de_la_tabla_y_vuelve_si_se_restaura()
    {
        await SeedAsync(Full);

        var without = CategoryRuleSet.Parse(RewriteRules(CatalogTestData.CategoriesJson, remove: "LECHE"));
        var removed = await SeedAsync(without);

        await using (var db = _database!.CreateContext())
        {
            Assert.Equal(1, removed.RulesRemoved);
            Assert.False(await db.CategoryRules.AnyAsync(r => r.Keyword == "LECHE", TestContext.Current.CancellationToken));
            Assert.Equal(TotalRules(Full) - 1, await db.CategoryRules.CountAsync(TestContext.Current.CancellationToken));
        }

        var restored = await SeedAsync(Full);
        Assert.Equal(1, restored.RulesAdded);
    }

    [PostgresFact]
    public async Task Cambiar_la_prioridad_en_el_json_actualiza_la_regla()
    {
        await SeedAsync(Full);

        var changed = CategoryRuleSet.Parse(RewriteRules(CatalogTestData.CategoriesJson, priorityOf: ("LECHE", 77)));
        var report = await SeedAsync(changed);

        await using var db = _database!.CreateContext();
        Assert.Equal(1, report.RulesUpdated);
        Assert.Equal(77, (await db.CategoryRules.SingleAsync(r => r.Keyword == "LECHE", TestContext.Current.CancellationToken)).Priority);
    }

    [PostgresFact]
    public async Task Una_categoria_quitada_sin_productos_se_borra_y_con_productos_se_conserva_con_advertencia()
    {
        await SeedAsync(Full);

        // "servicios" tendrá un producto asignado; "deportes" no.
        await using (var db = _database!.CreateContext())
        {
            var servicios = await db.Categories.SingleAsync(c => c.Slug == "servicios", TestContext.Current.CancellationToken);
            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                Code = "TEST-1",
                Description = "RECARGAS NORMALES",
                DisplayName = "Recargas Normales",
                CategoryId = servicios.Id,
                PublicPrice = 10,
                SyncedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var reduced = CategoryRuleSet.Parse(RemoveCategories(CatalogTestData.CategoriesJson, "servicios", "deportes"));
        var report = await SeedAsync(reduced);

        await using var check = _database!.CreateContext();
        Assert.Equal(1, report.CategoriesRemoved);
        Assert.False(await check.Categories.AnyAsync(c => c.Slug == "deportes", TestContext.Current.CancellationToken));
        Assert.True(await check.Categories.AnyAsync(c => c.Slug == "servicios", TestContext.Current.CancellationToken));
        Assert.Single(report.Warnings);
        Assert.Contains("servicios", report.Warnings[0]);
        Assert.True(await check.Categories.AnyAsync(c => c.Id == Category.OthersId, TestContext.Current.CancellationToken));

        // Sus reglas sí se quitan, aunque la categoría se conserve.
        var serviciosId = (await check.Categories.SingleAsync(c => c.Slug == "servicios", TestContext.Current.CancellationToken)).Id;
        Assert.False(await check.CategoryRules.AnyAsync(r => r.CategoryId == serviciosId, TestContext.Current.CancellationToken));
    }

    // Edita categories.json sin tocar el resto.
    private static string RewriteRules(string json, string? remove = null, (string Keyword, int Priority)? priorityOf = null)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        foreach (var category in root["categories"]!.AsArray())
        {
            var rules = category!["rules"]!.AsArray();
            foreach (var rule in rules.ToList())
            {
                var keyword = (string?)rule!["keyword"];
                if (keyword == remove)
                {
                    rules.Remove(rule);
                }
                else if (priorityOf is { } p && keyword == p.Keyword)
                {
                    rule["priority"] = p.Priority;
                }
            }
        }

        return root.ToJsonString();
    }

    private static string RemoveCategories(string json, params string[] slugs)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var categories = root["categories"]!.AsArray();
        foreach (var category in categories.ToList())
        {
            if (slugs.Contains((string?)category!["slug"]))
            {
                categories.Remove(category);
            }
        }

        return root.ToJsonString();
    }
}
