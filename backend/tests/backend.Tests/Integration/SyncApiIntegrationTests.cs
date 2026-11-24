using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using backend.Catalog;
using backend.Data;
using backend.Data.Entities;
using backend.Data.Seed;
using backend.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests.Integration;

/// <summary>
/// Protocolo de sincronización (US-06) contra PostgreSQL real: iniciar → lotes → completar.
/// Cada prueba usa su propia base temporal.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SyncApiIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    // Llave de prueba evidente; el backend solo recibe su hash.
    private const string AgentKey = "llave-de-ejemplo-no-usar-0000000000000000000";

    private TempDatabase? _database;
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (IntegrationEnvironment.IsConfigured)
        {
            _database = await TempDatabase.CreateAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------ apoyo

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _database!.ConnectionString);
            builder.UseSetting("Sync:AgentKeyHashes", SyncAgentKeyVerifier.ComputeHash(AgentKey));
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient AgentClient(WebApplicationFactory<Program> factory, string? key = AgentKey)
    {
        var client = factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add(SyncAgentKeyVerifier.HeaderName, key);
        }

        return client;
    }

    private static SyncBatchItem Item(string code, string? description = "LECHE ENT 1LT", double? stock = 5,
        string? status = "A", double? price = 25.5) => new(code, description, stock, status, price);

    private static List<SyncBatchItem> Items(int count, string prefix = "P") =>
        Enumerable.Range(0, count).Select(i => Item($"{prefix}{i:D5}", $"PRODUCTO {i} 1LT", i % 7, "A", 10 + i % 50)).ToList();

    private static string HashOf(IEnumerable<SyncBatchItem> items) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', items.Select(i => $"{i.Code}|{i.Description}|{i.Stock}|{i.Status}|{i.Price}")))));

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, Guid runId, int count, string? hash = null) =>
        await client.PostAsJsonAsync("/api/sync/runs", new StartSyncRunRequest(runId, count, hash, "pruebas"), Ct);

    private static async Task<HttpResponseMessage> BatchAsync(HttpClient client, Guid runId, int sequence, List<SyncBatchItem> items) =>
        await client.PostAsJsonAsync($"/api/sync/runs/{runId}/batches", new SyncBatchRequest(sequence, items), Ct);

    private static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid runId, int count, int batches,
        CancellationToken? ct = null) =>
        await client.PostAsJsonAsync($"/api/sync/runs/{runId}/complete", new CompleteSyncRunRequest(count, batches), ct ?? Ct);

    /// <summary>Protocolo completo como lo hace el agente. Devuelve la respuesta de inicio y, si hubo, la de completar.</summary>
    private static async Task<(StartSyncRunResponse Start, HttpResponseMessage? Complete)> SyncAsync(
        HttpClient client, List<SyncBatchItem> items, string? hash = null, int batchSize = 1000)
    {
        var runId = Guid.NewGuid();
        var start = await StartAsync(client, runId, items.Count, hash);
        Assert.True(start.IsSuccessStatusCode, await start.Content.ReadAsStringAsync(Ct));
        var started = (await start.Content.ReadFromJsonAsync<StartSyncRunResponse>(Ct))!;
        if (started.Status == SyncResponseStatuses.Unchanged)
        {
            return (started, null);
        }

        var batches = items.Chunk(batchSize).ToList();
        for (var i = 0; i < batches.Count; i++)
        {
            var batch = await BatchAsync(client, runId, i, [.. batches[i]]);
            Assert.Equal(HttpStatusCode.NoContent, batch.StatusCode);
        }

        return (started, await CompleteAsync(client, runId, items.Count, batches.Count));
    }

    private static async Task<SyncRunSummary> SyncOkAsync(HttpClient client, List<SyncBatchItem> items, string? hash = null)
    {
        var (_, complete) = await SyncAsync(client, items, hash);
        Assert.NotNull(complete);
        Assert.True(complete.StatusCode == HttpStatusCode.OK, await complete.Content.ReadAsStringAsync(Ct));
        return (await complete.Content.ReadFromJsonAsync<SyncRunSummary>(Ct))!;
    }

    private AppDbContext Db() => _database!.CreateContext();

    private static async Task SeedCategoriesAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogSeeder>()
            .SeedAsync(scope.ServiceProvider.GetRequiredService<CategoryRuleSet>(), Ct);
    }

    // ------------------------------------------------------------------ llave

    [PostgresFact]
    public async Task Sin_la_llave_correcta_todas_las_rutas_responden_401_y_no_cambian_nada()
    {
        var factory = CreateFactory();
        var runId = Guid.NewGuid();

        foreach (var key in new[] { null, "", "llave-incorrecta-no-usar", AgentKey + "x" })
        {
            using var client = AgentClient(factory, key);
            var responses = new[]
            {
                await client.GetAsync("/api/sync/ping", Ct),
                await StartAsync(client, runId, 1),
                await BatchAsync(client, runId, 0, [Item("X")]),
                await CompleteAsync(client, runId, 1, 1),
                await client.PostAsJsonAsync($"/api/sync/runs/{runId}/abort", new AbortSyncRunRequest("x"), Ct),
            };

            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync(Ct);
                Assert.DoesNotContain("AgentKey", body, StringComparison.OrdinalIgnoreCase);
            }
        }

        await using var db = Db();
        Assert.Equal(0, await db.SyncRuns.CountAsync(Ct));
        Assert.Equal(0, await db.Products.CountAsync(Ct));
    }

    [PostgresFact]
    public async Task Sin_hash_configurado_la_api_arranca_y_rechaza_el_agente()
    {
        var factory = CreateFactory(("Sync:AgentKeyHashes", ""));
        using var client = AgentClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sync/ping", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/api/health", Ct)).StatusCode);
    }

    [PostgresFact]
    public async Task Ping_con_la_llave_responde_200()
    {
        using var client = AgentClient(CreateFactory());
        var response = await client.GetFromJsonAsync<SyncPingResponse>("/api/sync/ping", Ct);
        Assert.True(response!.Ok);
    }

    // ------------------------------------------------------------------ aplicación

    [PostgresFact]
    public async Task La_primera_corrida_inserta_con_las_reglas_comerciales()
    {
        var factory = CreateFactory();
        await SeedCategoriesAsync(factory);
        using var client = AgentClient(factory);

        var summary = await SyncOkAsync(client,
        [
            Item("2050000117121", "LECHE ENT 1LT", 12.5, "A", 34.999999),
            Item("  2050000117121", "ARROZ BLANCO 2LB", 3, "A", 20),   // otro artículo: difiere solo por espacios
            Item("SINPRECIO", "AGUA PURA 1LT", 4, "A", null),
            Item("BAJA", "PAN BLANCO 680G", 4, "B", 15),
            Item("TEXTONULL", "NULL", 4, "A", 15),
            Item("DESCNULA", null, 4, "A", 15),
            Item("RELLENO", "AZUCAR 1LB", 500_000_000_000, "A", 15_000_000_000),
            Item("NEGATIVA", "SAL 1LB", -2.5, "A", 9),
        ]);

        Assert.Equal(8, summary.Read);
        Assert.Equal(8, summary.Inserted);
        Assert.Equal(0, summary.Updated);
        Assert.Equal(0, summary.Deactivated);
        Assert.Equal(0, summary.Rejected);

        await using var db = Db();
        var products = await db.Products.Include(p => p.Category).ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, Ct);
        Assert.Equal(8, products.Count);

        var leche = products["2050000117121"];
        Assert.Equal(35.00m, leche.PublicPrice);
        Assert.Equal(12.5m, leche.Stock);
        Assert.True(leche.IsActive);
        Assert.Equal("Leche Entera 1 L", leche.DisplayName);
        Assert.NotEqual(CategoryRuleSet.OthersSlug, leche.Category.Slug);

        Assert.Equal("ARROZ BLANCO 2LB", products["  2050000117121"].Description); // código exacto, sin recortar
        Assert.Equal(0m, products["SINPRECIO"].PublicPrice);
        Assert.False(products["BAJA"].IsActive);
        Assert.Equal("", products["TEXTONULL"].DisplayName);
        Assert.Equal("", products["DESCNULA"].Description);
        Assert.Equal("", products["DESCNULA"].DisplayName);
        Assert.Equal(15_000_000_000m, products["RELLENO"].PublicPrice);
        Assert.Equal(500_000_000_000m, products["RELLENO"].Stock);
        Assert.Equal(-2.5m, products["NEGATIVA"].Stock);

        var run = await db.SyncRuns.SingleAsync(Ct);
        Assert.Equal(SyncRunStatuses.Succeeded, run.Status);
        Assert.Equal(SyncRunModes.Full, run.Mode);
        Assert.Equal(8, run.ProductsInserted);
        Assert.NotNull(run.FinishedAt);
        Assert.Equal(0, await db.SyncRunItems.CountAsync(Ct)); // la preparación se borra

        // La visibilidad de business-rules §2 la aplica la API pública.
        var visible = await factory.CreateClient().GetFromJsonAsync<PagedResult<PublicProductDto>>("/api/products?pageSize=100", Ct);
        Assert.Equal(["Arroz Blanco 2 lb", "Leche Entera 1 L", "Sal 1 lb"], visible!.Items.Select(p => p.Name).Order().ToArray());
    }

    [PostgresFact]
    public async Task Repetir_la_corrida_no_duplica_ni_toca_updated_at()
    {
        var factory = CreateFactory(("Sync:FullRunMaxAgeHours", "0")); // fuerza el envío completo
        using var client = AgentClient(factory);
        var items = Items(30);
        items.Add(Item(" P00001", "OTRO CON ESPACIO"));

        await SyncOkAsync(client, items, HashOf(items));
        await using var before = Db();
        var updatedAt = await before.Products.ToDictionaryAsync(p => p.Code, p => (p.UpdatedAt, p.SyncedAt), StringComparer.Ordinal, Ct);

        var again = await SyncOkAsync(client, items, HashOf(items));

        Assert.Equal(0, again.Inserted);
        Assert.Equal(0, again.Updated);
        Assert.Equal(0, again.Deactivated);
        await using var after = Db();
        Assert.Equal(31, await after.Products.CountAsync(Ct));
        foreach (var p in await after.Products.ToListAsync(Ct))
        {
            Assert.Equal(updatedAt[p.Code], (p.UpdatedAt, p.SyncedAt));
        }
    }

    [PostgresFact]
    public async Task Con_la_misma_huella_la_corrida_queda_sin_cambios_y_no_se_envian_lotes()
    {
        var factory = CreateFactory();
        using var client = AgentClient(factory);
        var items = Items(25);
        var hash = HashOf(items);
        await SyncOkAsync(client, items, hash);

        var (start, complete) = await SyncAsync(client, items, hash);

        Assert.Equal(SyncResponseStatuses.Unchanged, start.Status);
        Assert.Null(complete);
        await using var db = Db();
        var runs = await db.SyncRuns.OrderBy(r => r.StartedAt).ToListAsync(Ct);
        Assert.Equal([SyncRunModes.Full, SyncRunModes.Unchanged], runs.Select(r => r.Mode).ToArray());
        Assert.All(runs, r => Assert.Equal(SyncRunStatuses.Succeeded, r.Status));
        Assert.Equal(25, runs[1].ProductsRead);

        // Otra huella (el inventario cambió): envío completo.
        items[0] = items[0] with { Price = 99 };
        var (changedStart, changed) = await SyncAsync(client, items, HashOf(items));
        Assert.Equal(SyncResponseStatuses.Running, changedStart.Status);
        Assert.Equal(1, (await changed!.Content.ReadFromJsonAsync<SyncRunSummary>(Ct))!.Updated);
    }

    [PostgresFact]
    public async Task Desactiva_los_que_no_vinieron_y_los_reactiva_si_vuelven()
    {
        using var client = AgentClient(CreateFactory());
        var items = Items(30);
        await SyncOkAsync(client, items);

        var withoutTwo = items.Where(i => i.Code is not ("P00003" or "P00007")).ToList();
        var summary = await SyncOkAsync(client, withoutTwo);
        Assert.Equal(2, summary.Deactivated);
        Assert.Equal(0, summary.Updated);

        await using (var db = Db())
        {
            Assert.Equal(30, await db.Products.CountAsync(Ct)); // no se borran
            Assert.Equal(["P00003", "P00007"], await db.Products.Where(p => !p.IsActive).Select(p => p.Code).OrderBy(c => c).ToArrayAsync(Ct));
        }

        var back = await SyncOkAsync(client, items);
        Assert.Equal(2, back.Updated);
        Assert.Equal(0, back.Deactivated);
        await using var db2 = Db();
        Assert.Equal(30, await db2.Products.CountAsync(p => p.IsActive, Ct));
    }

    [PostgresFact]
    public async Task Solo_una_descripcion_nueva_recalcula_nombre_y_categoria()
    {
        var factory = CreateFactory();
        await SeedCategoriesAsync(factory);
        using var client = AgentClient(factory);
        await SyncOkAsync(client, [Item("A", "LECHE ENT 1LT", price: 10), Item("B", "LECHE ENT 1LT", price: 10)]);

        // Marca: si se recalcula, desaparece.
        await using (var db = Db())
        {
            await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, "MARCA"), Ct);
        }

        var summary = await SyncOkAsync(client, [Item("A", "LECHE ENT 1LT", price: 12), Item("B", "ARROZ BLANCO 2LB", price: 10)]);
        Assert.Equal(2, summary.Updated);

        await using var after = Db();
        var products = await after.Products.Include(p => p.Category).ToDictionaryAsync(p => p.Code, Ct);
        Assert.Equal("MARCA", products["A"].DisplayName); // solo cambió el precio
        Assert.Equal(12m, products["A"].PublicPrice);
        Assert.Equal("Arroz Blanco 2 lb", products["B"].DisplayName);
        Assert.NotEqual(products["A"].CategoryId, products["B"].CategoryId);
    }

    [PostgresFact]
    public async Task Los_articulos_fuera_de_rango_se_rechazan_sin_tocar_ni_desactivar_el_producto()
    {
        using var client = AgentClient(CreateFactory());
        await SyncOkAsync(client, [Item("A", price: 10), Item("B", price: 10)]);

        var summary = await SyncOkAsync(client,
        [
            Item("A", price: 1e17),                 // ya existía: se conserva tal cual
            Item("B", price: 11),
            Item(new string('9', 51), price: 5),   // código más largo que la columna
            Item("   ", price: 5),                  // código solo con espacios
        ]);

        Assert.Equal(4, summary.Read);
        Assert.Equal(3, summary.Rejected);
        Assert.Equal(1, summary.Updated);
        Assert.Equal(0, summary.Deactivated);
        Assert.Contains("A", summary.RejectedCodes);

        await using var db = Db();
        var a = await db.Products.SingleAsync(p => p.Code == "A", Ct);
        Assert.True(a.IsActive);
        Assert.Equal(10m, a.PublicPrice);
        Assert.Equal(3, (await db.SyncRuns.OrderByDescending(r => r.StartedAt).FirstAsync(Ct)).ProductsRejected);
    }

    // ------------------------------------------------------------------ freno y validaciones

    [PostgresFact]
    public async Task El_freno_de_seguridad_no_cambia_nada_y_deja_la_corrida_fallida()
    {
        using var client = AgentClient(CreateFactory());
        var items = Items(40);
        await SyncOkAsync(client, items);

        // Solo llegan 5 de 40: se desactivarían 35 (más del 10 % y más de 20).
        var partial = items.Take(5).Select(i => i with { Price = 999 }).ToList();
        var (_, complete) = await SyncAsync(client, partial);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, complete!.StatusCode);
        var problem = (await complete.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        Assert.Equal(SyncProblemTypes.SafetyBrake, problem.Type);
        Assert.Contains("35 de 40", problem.Detail);

        await using var db = Db();
        Assert.Equal(40, await db.Products.CountAsync(p => p.IsActive, Ct));
        Assert.Equal(0, await db.Products.CountAsync(p => p.PublicPrice == 999, Ct)); // ni siquiera los precios
        var run = await db.SyncRuns.OrderByDescending(r => r.StartedAt).FirstAsync(Ct);
        Assert.Equal(SyncRunStatuses.Failed, run.Status);
        Assert.Contains("Freno de seguridad", run.ErrorMessage);
        Assert.Equal(0, await db.SyncRunItems.CountAsync(Ct));
    }

    [PostgresFact]
    public async Task Una_corrida_sin_articulos_frena()
    {
        using var client = AgentClient(CreateFactory());
        var runId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await StartAsync(client, runId, 0)).StatusCode);

        var complete = await CompleteAsync(client, runId, 0, 0);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, complete.StatusCode);
        Assert.Equal(SyncProblemTypes.SafetyBrake, (await complete.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Type);
    }

    [PostgresFact]
    public async Task Si_el_conteo_no_cuadra_la_corrida_falla_sin_cambios()
    {
        using var client = AgentClient(CreateFactory());
        var runId = Guid.NewGuid();
        await StartAsync(client, runId, 3);
        await BatchAsync(client, runId, 0, [Item("A"), Item("B")]);

        var complete = await CompleteAsync(client, runId, 3, 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, complete.StatusCode);
        Assert.Equal(SyncProblemTypes.CountMismatch, (await complete.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Type);
        await using var db = Db();
        Assert.Equal(0, await db.Products.CountAsync(Ct));
        Assert.Equal(SyncRunStatuses.Failed, (await db.SyncRuns.SingleAsync(Ct)).Status);

        // Una corrida fallida ya no admite lotes ni completar.
        Assert.Equal(HttpStatusCode.Conflict, (await BatchAsync(client, runId, 1, [Item("C")])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CompleteAsync(client, runId, 3, 1)).StatusCode);
    }

    [PostgresFact]
    public async Task Reenviar_un_lote_lo_reemplaza_y_un_codigo_repetido_entre_lotes_falla_la_corrida()
    {
        using var client = AgentClient(CreateFactory());
        var runId = Guid.NewGuid();
        await StartAsync(client, runId, 2);

        Assert.Equal(HttpStatusCode.NoContent, (await BatchAsync(client, runId, 0, [Item("A", price: 1), Item("B")])).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await BatchAsync(client, runId, 0, [Item("A", price: 2), Item("B")])).StatusCode);
        await using (var db = Db())
        {
            Assert.Equal(2, await db.SyncRunItems.CountAsync(Ct));
            Assert.Equal(2, (await db.SyncRunItems.SingleAsync(i => i.Code == "A", Ct)).Price);
        }

        var duplicate = await BatchAsync(client, runId, 1, [Item("B")]);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(SyncProblemTypes.DuplicateCode, (await duplicate.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Type);
        await using var after = Db();
        Assert.Equal(SyncRunStatuses.Failed, (await after.SyncRuns.SingleAsync(Ct)).Status);
        Assert.Equal(0, await after.SyncRunItems.CountAsync(Ct));
    }

    [PostgresFact]
    public async Task Un_lote_invalido_se_rechaza_con_un_error_claro()
    {
        using var client = AgentClient(CreateFactory());
        var runId = Guid.NewGuid();
        await StartAsync(client, runId, 3);

        var cases = new List<SyncBatchItem>[]
        {
            [],
            [Item("A"), Item("A")],                       // repetido dentro del lote
            [new SyncBatchItem(null, "X", 1, "A", 1)],     // sin código
            Items(2001),                                   // más que el máximo
        };

        foreach (var items in cases)
        {
            var response = await BatchAsync(client, runId, 0, items);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Detail));
        }
    }

    [PostgresFact]
    public async Task Reintentar_completar_devuelve_el_mismo_resumen()
    {
        using var client = AgentClient(CreateFactory());
        var runId = Guid.NewGuid();
        await StartAsync(client, runId, 2);
        await BatchAsync(client, runId, 0, [Item("A"), Item("B")]);

        var first = await (await CompleteAsync(client, runId, 2, 1)).Content.ReadFromJsonAsync<SyncRunSummary>(Ct);
        var retry = await CompleteAsync(client, runId, 2, 1);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var second = (await retry.Content.ReadFromJsonAsync<SyncRunSummary>(Ct))!;
        Assert.Equal(first!.Inserted, second.Inserted);
        Assert.Equal(2, second.Inserted);

        // Reintentar el inicio con el mismo runId no crea otra corrida.
        Assert.Equal(HttpStatusCode.Conflict, (await StartAsync(client, runId, 2)).StatusCode);
        await using var db = Db();
        Assert.Equal(1, await db.SyncRuns.CountAsync(Ct));
    }

    // ------------------------------------------------------------------ concurrencia y limpieza

    [PostgresFact]
    public async Task Una_corrida_abandonada_se_marca_fallida_y_una_reciente_bloquea()
    {
        using var client = AgentClient(CreateFactory());
        var stale = Guid.NewGuid();
        await StartAsync(client, stale, 1);
        await BatchAsync(client, stale, 0, [Item("A")]);

        // Reintentar el inicio de la misma corrida es idempotente; otra corrida reciente recibe 409.
        Assert.Equal(HttpStatusCode.OK, (await StartAsync(client, stale, 1)).StatusCode);
        var blocked = await StartAsync(client, Guid.NewGuid(), 1);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(SyncProblemTypes.RunInProgress, (await blocked.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Type);

        await using (var db = Db())
        {
            await db.SyncRuns.ExecuteUpdateAsync(s => s.SetProperty(r => r.StartedAt, DateTime.UtcNow.AddMinutes(-16)), Ct);
        }

        Assert.Equal(HttpStatusCode.Created, (await StartAsync(client, Guid.NewGuid(), 1)).StatusCode);

        await using var after = Db();
        var old = await after.SyncRuns.SingleAsync(r => r.Id == stale, Ct);
        Assert.Equal(SyncRunStatuses.Failed, old.Status);
        Assert.Contains("abandonada", old.ErrorMessage);
        Assert.Equal(0, await after.SyncRunItems.CountAsync(i => i.RunId == stale, Ct));
    }

    [PostgresFact]
    public async Task Al_iniciar_se_borran_las_corridas_sin_cambios_de_mas_de_30_dias()
    {
        using var client = AgentClient(CreateFactory());
        var now = DateTime.UtcNow;
        var oldUnchanged = new SyncRun { Id = Guid.NewGuid(), Status = SyncRunStatuses.Succeeded, Mode = SyncRunModes.Unchanged, StartedAt = now.AddDays(-31), FinishedAt = now.AddDays(-31) };
        var recentUnchanged = new SyncRun { Id = Guid.NewGuid(), Status = SyncRunStatuses.Succeeded, Mode = SyncRunModes.Unchanged, StartedAt = now.AddDays(-29), FinishedAt = now.AddDays(-29) };
        var oldFull = new SyncRun { Id = Guid.NewGuid(), Status = SyncRunStatuses.Succeeded, Mode = SyncRunModes.Full, StartedAt = now.AddDays(-60), FinishedAt = now.AddDays(-60) };
        var oldFailed = new SyncRun { Id = Guid.NewGuid(), Status = SyncRunStatuses.Failed, Mode = SyncRunModes.Full, StartedAt = now.AddDays(-60), FinishedAt = now.AddDays(-60) };
        await using (var db = Db())
        {
            db.SyncRuns.AddRange(oldUnchanged, recentUnchanged, oldFull, oldFailed);
            await db.SaveChangesAsync(Ct);
        }

        await StartAsync(client, Guid.NewGuid(), 1);

        await using var after = Db();
        var ids = await after.SyncRuns.Select(r => r.Id).ToListAsync(Ct);
        Assert.DoesNotContain(oldUnchanged.Id, ids);
        Assert.Contains(recentUnchanged.Id, ids);
        Assert.Contains(oldFull.Id, ids);
        Assert.Contains(oldFailed.Id, ids);
    }

    // ------------------------------------------------------------------ carga real y cortes del proxy

    [PostgresFact]
    public async Task Carga_de_18500_articulos_registra_cuanto_tarda_completar()
    {
        using var client = AgentClient(CreateFactory());
        var factory = _factories[0];
        await SeedCategoriesAsync(factory);

        var items = SyntheticInventory(18_500);
        var runId = Guid.NewGuid();
        await StartAsync(client, runId, items.Count);

        var sending = Stopwatch.StartNew();
        var batches = items.Chunk(1000).ToList();
        for (var i = 0; i < batches.Count; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await BatchAsync(client, runId, i, [.. batches[i]])).StatusCode);
        }

        sending.Stop();

        var completing = Stopwatch.StartNew();
        var complete = await CompleteAsync(client, runId, items.Count, batches.Count);
        completing.Stop();

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var summary = (await complete.Content.ReadFromJsonAsync<SyncRunSummary>(Ct))!;
        Assert.Equal(18_500, summary.Inserted);

        output.WriteLine($"Primera carga: {batches.Count} lotes en {sending.Elapsed.TotalSeconds:N1} s; " +
                         $"completar tardó {completing.Elapsed.TotalSeconds:N2} s.");

        // Segunda corrida completa sin cambios (lo que pasa una vez al día).
        var again = Stopwatch.StartNew();
        var second = await SyncOkAsync(client, items);
        again.Stop();
        Assert.Equal(0, second.Updated);
        output.WriteLine($"Corrida completa sin cambios (lotes + completar): {again.Elapsed.TotalSeconds:N2} s.");

        Assert.True(completing.Elapsed < TimeSpan.FromSeconds(30), "Completar debe quedar por debajo del timeout de 30 s del proxy de Next.js.");
    }

    [PostgresFact]
    public async Task Si_el_cliente_se_desconecta_al_completar_la_transaccion_termina_y_el_reintento_recibe_el_resumen()
    {
        using var client = AgentClient(CreateFactory());
        var items = SyntheticInventory(8_000);
        var runId = Guid.NewGuid();
        await StartAsync(client, runId, items.Count);
        var batches = items.Chunk(1000).ToList();
        for (var i = 0; i < batches.Count; i++)
        {
            await BatchAsync(client, runId, i, [.. batches[i]]);
        }

        // Simula el corte del proxy: el cliente abandona la solicitud a mitad de la transacción.
        using var cut = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cut.CancelAfter(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CompleteAsync(client, runId, items.Count, batches.Count, cut.Token));

        // El reintento del agente espera el candado y recibe el resumen confirmado.
        var retry = await CompleteAsync(client, runId, items.Count, batches.Count);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(8_000, (await retry.Content.ReadFromJsonAsync<SyncRunSummary>(Ct))!.Inserted);

        await using var db = Db();
        Assert.Equal(8_000, await db.Products.CountAsync(Ct));
        Assert.Equal(SyncRunStatuses.Succeeded, (await db.SyncRuns.SingleAsync(Ct)).Status);
    }

    /// <summary>Inventario parecido al real: descripciones variadas, códigos con espacios, sin precio, de baja, decimales.</summary>
    private static List<SyncBatchItem> SyntheticInventory(int count)
    {
        string[] words = ["LECHE ENT", "ARROZ BLANCO", "JUGO NARANJA", "GALLETA CHOKIS", "DETERGENTE POLVO", "PANAL HUGGIES",
            "CAFE MOLIDO", "SOPA MAGGI", "ACEITE VEGETAL", "QUESO SEMISECO", "CUADERNO ESPIRAL", "FOCO LED"];
        string[] sizes = ["1LT", "2LB", "500G", "100ML", "UNIDAD", "740ML"];
        var random = new Random(20261124);

        return Enumerable.Range(0, count).Select(i => new SyncBatchItem(
            Code: i % 1000 == 0 ? $"  {2050000000000 + i}" : $"{2050000000000 + i}",
            Description: $"{words[i % words.Length]} {sizes[i % sizes.Length]} {i}",
            Stock: i % 50 == 0 ? 500_000_000_000 : Math.Round(random.NextDouble() * 40 - 2, 3),
            Status: i % 20 == 0 ? "B" : "A",
            Price: i % 150 == 0 ? null : Math.Round(random.NextDouble() * 300, 6))).ToList();
    }
}
