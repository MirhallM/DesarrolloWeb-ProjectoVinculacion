using System.Diagnostics;
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

    private static readonly DisplayDictionary DefaultDictionary = DisplayDictionary.LoadEmbedded();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<CatalogSeedReport> SeedAsync(
        CategoryRuleSet rules, DisplayDictionary? dictionary = null, TimeSpan? lockTimeout = null)
    {
        await using var db = _database!.CreateContext();
        return await new CatalogSeeder(db, NullLogger<CatalogSeeder>.Instance)
            .SeedAsync(rules, dictionary ?? DefaultDictionary, Ct, lockTimeout);
    }

    /// <summary>Inserta productos con categoría y nombre calculados con las reglas completas, como los dejaría la sincronización.</summary>
    private async Task AddProductsAsync(params (string Code, string Description)[] items)
    {
        await using var db = _database!.CreateContext();
        var ids = await db.Categories.ToDictionaryAsync(c => c.Slug, c => c.Id, StringComparer.Ordinal, Ct);
        var fields = new CatalogProductFields(new Categorizer(Full), new DisplayNamer(DefaultDictionary));
        foreach (var (code, description) in items)
        {
            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                Code = code,
                Description = description,
                DisplayName = fields.GetDisplayName(description),
                CategoryId = fields.GetCategoryId(description, ids),
                PublicPrice = 10,
                SyncedAt = DateTime.UtcNow.AddHours(-1),
            });
        }

        await db.SaveChangesAsync(Ct);
    }

    private async Task<Dictionary<string, Product>> ProductsAsync()
    {
        await using var db = _database!.CreateContext();
        return await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, Ct);
    }

    private static string CategorySlug(CategoryRuleSet rules, string description) => new Categorizer(rules).Categorize(description).Slug;

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
    public async Task Una_categoria_quitada_se_borra_y_sus_productos_pasan_a_la_que_indiquen_las_reglas()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("R1", "RECARGAS NORMALES CLARO"), ("A1", "ARROZ BLANCO 2LB"));
        var before = await ProductsAsync();

        // "servicios" tiene un producto asignado; "deportes" no.
        var reduced = CategoryRuleSet.Parse(RemoveCategories(CatalogTestData.CategoriesJson, "servicios", "deportes"));
        var report = await SeedAsync(reduced);

        await using var check = _database!.CreateContext();
        Assert.Equal(2, report.CategoriesRemoved);
        Assert.Empty(report.Warnings);
        Assert.False(await check.Categories.AnyAsync(c => c.Slug == "deportes", Ct));
        Assert.False(await check.Categories.AnyAsync(c => c.Slug == "servicios", Ct));
        Assert.True(await check.Categories.AnyAsync(c => c.Id == Category.OthersId, Ct));
        Assert.Equal(reduced.Categories.Count, await check.Categories.CountAsync(Ct));

        // El producto pasa a la categoría que ahora le corresponde y el otro no se toca.
        var after = await ProductsAsync();
        var expected = await check.Categories.SingleAsync(c => c.Slug == CategorySlug(reduced, "RECARGAS NORMALES CLARO"), Ct);
        Assert.NotEqual(before["R1"].CategoryId, after["R1"].CategoryId);
        Assert.Equal(expected.Id, after["R1"].CategoryId);
        Assert.Equal(before["A1"].UpdatedAt, after["A1"].UpdatedAt);
    }

    [PostgresFact]
    public async Task Una_categoria_nueva_puede_reutilizar_el_nombre_de_una_retirada_que_tenia_productos()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("R1", "RECARGAS NORMALES CLARO"));
        var servicios = Full.Categories.Single(c => c.Slug == "servicios");

        // Se quita "servicios" y se crea otra categoría con su mismo nombre y posición.
        var root = System.Text.Json.Nodes.JsonNode.Parse(RemoveCategories(CatalogTestData.CategoriesJson, "servicios"))!;
        root["categories"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(
            $$"""{ "slug": "recargas", "name": "{{servicios.Name}}", "position": {{servicios.Position}}, "rules": [ { "keyword": "RECARGAS", "priority": 50 } ] }"""));
        var renamed = CategoryRuleSet.Parse(root.ToJsonString());

        var report = await SeedAsync(renamed);

        await using var check = _database!.CreateContext();
        Assert.Empty(report.Warnings);
        Assert.False(await check.Categories.AnyAsync(c => c.Slug == "servicios", Ct));
        var recargas = await check.Categories.SingleAsync(c => c.Slug == "recargas", Ct);
        Assert.Equal(servicios.Name, recargas.Name);
        Assert.Equal(recargas.Id, (await ProductsAsync())["R1"].CategoryId);
        Assert.False(await check.Categories.AnyAsync(c => c.Name.StartsWith("~retirada~"), Ct));
    }

    [PostgresFact]
    public async Task Cambiar_una_regla_recategoriza_solo_los_productos_afectados_sin_tocar_updated_at_ni_synced_at_de_los_demas()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("LE", "LECHE ENT 1LT"), ("AR", "ARROZ BLANCO 2LB"));
        var before = await ProductsAsync();

        var withoutLeche = CategoryRuleSet.Parse(RewriteRules(CatalogTestData.CategoriesJson, remove: "LECHE"));
        var report = await SeedAsync(withoutLeche);

        var after = await ProductsAsync();
        await using var db = _database!.CreateContext();
        var expected = (await db.Categories.SingleAsync(c => c.Slug == CategorySlug(withoutLeche, "LECHE ENT 1LT"), Ct)).Id;

        Assert.NotEqual(before["LE"].CategoryId, expected); // la prueba solo vale si quitar la regla cambia la categoría
        Assert.Equal(expected, after["LE"].CategoryId);
        Assert.Equal(1, report.ProductsCategoryChanged);
        Assert.Equal(0, report.ProductsDisplayNameChanged);
        Assert.True(after["LE"].UpdatedAt > before["LE"].UpdatedAt);

        // El otro producto no se tocó. Ninguno cambia synced_at: la recategorización no es una sincronización.
        Assert.Equal(before["AR"].UpdatedAt, after["AR"].UpdatedAt);
        Assert.Equal(before["AR"].CategoryId, after["AR"].CategoryId);
        Assert.Equal(before["LE"].SyncedAt, after["LE"].SyncedAt);
        Assert.Equal(before["AR"].SyncedAt, after["AR"].SyncedAt);
    }

    [PostgresFact]
    public async Task Cambiar_el_diccionario_recalcula_display_name_y_no_cambia_la_categoria()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("LE", "LECHE ENT 1LT"), ("SA", "SAL REFINADA"));
        var before = await ProductsAsync();

        var root = System.Text.Json.Nodes.JsonNode.Parse(CatalogTestData.DictionaryJson)!;
        root["units"]!["LT"] = "Lts";
        var changed = DisplayDictionary.Parse(root.ToJsonString());
        var expected = new DisplayNamer(changed).GetDisplayName("LECHE ENT 1LT");
        Assert.NotEqual(before["LE"].DisplayName, expected);

        var report = await SeedAsync(Full, changed);

        var after = await ProductsAsync();
        Assert.Equal(expected, after["LE"].DisplayName);
        Assert.Equal(1, report.ProductsDisplayNameChanged);
        Assert.Equal(0, report.ProductsCategoryChanged);
        Assert.Equal(before["LE"].CategoryId, after["LE"].CategoryId);
        Assert.Equal(before["SA"].UpdatedAt, after["SA"].UpdatedAt);
        Assert.Equal(before["LE"].SyncedAt, after["LE"].SyncedAt);
    }

    [PostgresFact]
    public async Task Recategorizar_dos_veces_con_las_mismas_reglas_no_cambia_nada()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("LE", "LECHE ENT 1LT"), ("AR", "ARROZ BLANCO 2LB"));
        var withoutLeche = CategoryRuleSet.Parse(RewriteRules(CatalogTestData.CategoriesJson, remove: "LECHE"));

        var first = await SeedAsync(withoutLeche);
        var afterFirst = await ProductsAsync();
        var second = await SeedAsync(withoutLeche);
        var afterSecond = await ProductsAsync();

        Assert.Equal(1, first.ProductsCategoryChanged);
        Assert.Equal(0, second.ProductsCategoryChanged);
        Assert.Equal(0, second.ProductsDisplayNameChanged);
        foreach (var code in afterFirst.Keys)
        {
            Assert.Equal(afterFirst[code].UpdatedAt, afterSecond[code].UpdatedAt);
        }
    }

    [PostgresFact]
    public async Task Un_producto_con_descripcion_vacia_queda_con_display_name_vacio_al_recategorizar()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("NU", "NULL"));
        await using (var db = _database!.CreateContext())
        {
            await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, "Basura"), Ct);
        }

        var report = await SeedAsync(Full);

        Assert.Equal(1, report.ProductsDisplayNameChanged);
        Assert.Equal(string.Empty, (await ProductsAsync())["NU"].DisplayName);
    }

    [PostgresFact]
    public async Task La_siembra_espera_a_una_escritura_en_curso_sobre_products_y_despues_aplica()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("LE", "LECHE ENT 1LT"));
        var withoutLeche = CategoryRuleSet.Parse(RewriteRules(CatalogTestData.CategoriesJson, remove: "LECHE"));

        // Simula un "completar" en curso: una transacción abierta con el bloqueo de quien escribe en products.
        await using var other = _database!.CreateContext();
        await using var writing = await other.Database.BeginTransactionAsync(Ct);
        Task<CatalogSeedReport>? seeding = null;
        try
        {
            await other.Database.ExecuteSqlRawAsync("LOCK TABLE products IN ROW EXCLUSIVE MODE", Ct);

            // Con un tope propio: aunque algo falle, la siembra termina (con timeout) en lugar de esperar 60 s.
            seeding = SeedAsync(withoutLeche, lockTimeout: TimeSpan.FromSeconds(20));
            await Task.Delay(TimeSpan.FromSeconds(2), Ct);
            Assert.False(seeding.IsCompleted, "La siembra debería esperar a que termine la escritura en curso.");
        }
        finally
        {
            // Pase lo que pase se libera el bloqueo; si la aserción falló, además se espera a que la siembra termine
            // para no dejar una tarea sin observar.
            await writing.RollbackAsync(CancellationToken.None);
            if (seeding is not null)
            {
                await Task.WhenAny(seeding, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None));
            }
        }

        var report = await seeding!.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(1, report.ProductsCategoryChanged);
    }

    [PostgresFact]
    public async Task El_timeout_de_comando_de_la_siembra_deja_margen_para_que_venza_primero_el_bloqueo()
    {
        // Npgsql corta los comandos a los 30 s; el bloqueo por omisión espera 60 s. Sin este margen el corte del comando
        // llegaría antes y el error sería un NpgsqlException genérico en lugar del mensaje claro de la siembra.
        Assert.True(CatalogSeeder.DefaultLockTimeout > TimeSpan.FromSeconds(30));
        Assert.True(CatalogSeeder.CommandTimeoutFor(CatalogSeeder.DefaultLockTimeout) > CatalogSeeder.DefaultLockTimeout);

        await using var db = _database!.CreateContext();
        var seeder = new CatalogSeeder(db, NullLogger<CatalogSeeder>.Instance);
        await seeder.SeedAsync(Full, DefaultDictionary, Ct);

        Assert.Equal((int)CatalogSeeder.CommandTimeoutFor(CatalogSeeder.DefaultLockTimeout).TotalSeconds, db.Database.GetCommandTimeout());
    }

    [PostgresFact]
    public async Task Si_una_escritura_no_termina_la_siembra_falla_por_timeout_en_pocos_segundos_y_no_cambia_nada()
    {
        await SeedAsync(Full);
        await AddProductsAsync(("LE", "LECHE ENT 1LT"));
        var withoutLeche = CategoryRuleSet.Parse(RewriteRules(CatalogTestData.CategoriesJson, remove: "LECHE"));
        var before = await ProductsAsync();
        int rulesBefore;
        await using (var db = _database!.CreateContext())
        {
            rulesBefore = await db.CategoryRules.CountAsync(Ct);
        }

        // Una escritura que nunca termina: la transacción se mantiene abierta hasta el finally.
        await using var other = _database!.CreateContext();
        await using var writing = await other.Database.BeginTransactionAsync(Ct);
        try
        {
            await other.Database.ExecuteSqlRawAsync("LOCK TABLE products IN ROW EXCLUSIVE MODE", Ct);

            var clock = Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<TimeoutException>(
                () => SeedAsync(withoutLeche, lockTimeout: TimeSpan.FromSeconds(2)));
            clock.Stop();

            // Falla cerca de los 2 s configurados, no a los 60 s por omisión (eso indicaría que SET LOCAL se ignoró).
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(10));
            Assert.Contains("products", error.Message);
            Assert.Contains("No se cambió nada", error.Message);
            var postgres = Assert.IsType<Npgsql.PostgresException>(error.InnerException);
            Assert.Equal(Npgsql.PostgresErrorCodes.LockNotAvailable, postgres.SqlState);
        }
        finally
        {
            await writing.RollbackAsync(CancellationToken.None);
        }

        // La transacción de la siembra se revirtió: ni reglas ni productos cambiaron.
        await using (var check = _database!.CreateContext())
        {
            Assert.Equal(rulesBefore, await check.CategoryRules.CountAsync(Ct));
        }

        var after = await ProductsAsync();
        Assert.Equal(before["LE"].CategoryId, after["LE"].CategoryId);
        Assert.Equal(before["LE"].UpdatedAt, after["LE"].UpdatedAt);

        // Y cuando la escritura termina, el siguiente intento sí aplica (el tiempo de espera no quedó pegado a la conexión).
        var retry = await SeedAsync(withoutLeche);
        Assert.Equal(1, retry.ProductsCategoryChanged);
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
