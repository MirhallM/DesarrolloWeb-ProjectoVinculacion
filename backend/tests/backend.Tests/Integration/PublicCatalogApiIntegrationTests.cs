using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Catalog;
using backend.Data.Entities;
using backend.Data.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests.Integration;

/// <summary>
/// Pruebas de integración contra PostgreSQL real para los endpoints públicos de consulta (US-08).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PublicCatalogApiIntegrationTests : IAsyncLifetime
{
    private TempDatabase? _database;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        if (!IntegrationEnvironment.IsConfigured)
        {
            return;
        }

        _database = await TempDatabase.CreateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection", _database.ConnectionString));
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task GetProducts_pagina_ordena_y_excluye_productos_ocultos_y_campos_privados()
    {
        var seed = await SeedCatalogAsync();
        var ct = TestContext.Current.CancellationToken;

        // 1. Consulta por defecto: page=1, pageSize=24, sort=name asc
        var defaultResponse = await _client!.GetAsync("/api/products", ct);
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);

        var rawJson = await defaultResponse.Content.ReadAsStringAsync(ct);
        AssertNoSensitiveFields(rawJson);

        var pageResult = JsonSerializer.Deserialize<PagedResult<PublicProductDto>>(rawJson, JsonOptions)!;
        Assert.Equal(1, pageResult.Page);
        Assert.Equal(24, pageResult.PageSize);
        Assert.Equal(4, pageResult.TotalCount);
        Assert.Equal(4, pageResult.Items.Count);
        Assert.Equal(["Agua Pura 1 L", "Arroz Blanco 2 lb", "Leche Entera 1 L", "Producto Sin Categoría"],
            pageResult.Items.Select(x => x.Name).ToArray());

        // Verificar precios (2 decimales), disponibilidad e imageUrl
        var agua = pageResult.Items.Single(x => x.Id == seed.VisibleWithImageId);
        Assert.Equal(18.50m, agua.Price);
        Assert.True(agua.Available);
        Assert.Equal("bebidas", agua.Category.Slug);
        Assert.Equal("Bebidas", agua.Category.Name);
        Assert.Equal("/images/agua-pura.jpg", agua.ImageUrl);

        var arroz = pageResult.Items.Single(x => x.Id == seed.VisibleZeroStockId);
        Assert.False(arroz.Available);
        Assert.Null(arroz.ImageUrl);

        var leche = pageResult.Items.Single(x => x.Id == seed.VisibleNegativeStockId);
        Assert.False(leche.Available);

        // 2. Paginación: pageSize=2, page=2
        var paged = await _client.GetFromJsonAsync<PagedResult<PublicProductDto>>(
            "/api/products?page=2&pageSize=2", JsonOptions, ct);
        Assert.NotNull(paged);
        Assert.Equal(2, paged.Page);
        Assert.Equal(2, paged.PageSize);
        Assert.Equal(4, paged.TotalCount);
        Assert.Equal(["Leche Entera 1 L", "Producto Sin Categoría"],
            paged.Items.Select(x => x.Name).ToArray());

        // 3. Ordenamiento por precio descendente
        var byPriceDesc = await _client.GetFromJsonAsync<PagedResult<PublicProductDto>>(
            "/api/products?sort=price&order=desc", JsonOptions, ct);
        Assert.NotNull(byPriceDesc);
        Assert.Equal([75.25m, 45.00m, 32.10m, 18.50m],
            byPriceDesc.Items.Select(x => x.Price).ToArray());
    }

    [PostgresFact]
    public async Task GetProductById_devuelve_200_para_visible_y_404_para_ocultos_o_inexistentes()
    {
        var seed = await SeedCatalogAsync();
        var ct = TestContext.Current.CancellationToken;

        // Producto visible -> 200 OK sin campos privados
        var okResponse = await _client!.GetAsync($"/api/products/{seed.VisibleWithImageId}", ct);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);

        var rawJson = await okResponse.Content.ReadAsStringAsync(ct);
        AssertNoSensitiveFields(rawJson);

        var dto = JsonSerializer.Deserialize<PublicProductDto>(rawJson, JsonOptions)!;
        Assert.Equal(seed.VisibleWithImageId, dto.Id);
        Assert.Equal("Agua Pura 1 L", dto.Name);
        Assert.Equal("/images/agua-pura.jpg", dto.ImageUrl);

        // Productos ocultos -> 404 ProblemDetails
        foreach (var hiddenId in seed.HiddenIds)
        {
            var hiddenResponse = await _client.GetAsync($"/api/products/{hiddenId}", ct);
            Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
            Assert.Equal("application/problem+json", hiddenResponse.Content.Headers.ContentType?.MediaType);
        }

        // Id inexistente -> 404 ProblemDetails
        var missingResponse = await _client.GetAsync($"/api/products/{Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [PostgresFact]
    public async Task GetCategories_ordena_por_posicion_con_otros_al_final_y_conteo_coincide_con_productos_visibles()
    {
        await SeedCatalogAsync();
        var ct = TestContext.Current.CancellationToken;

        var categories = await _client!.GetFromJsonAsync<List<PublicCategoryDto>>("/api/categories", JsonOptions, ct);
        var products = await _client!.GetFromJsonAsync<PagedResult<PublicProductDto>>("/api/products?pageSize=100", JsonOptions, ct);

        Assert.NotNull(categories);
        Assert.NotNull(products);

        // Orden: abarrotes (pos 1), bebidas (pos 2), lacteos (pos 3), snacks (pos 4, 0 visibles), otros (al final)
        Assert.Equal(["abarrotes", "bebidas", "lacteos", "snacks", "otros"], categories.Select(c => c.Slug).ToArray());
        Assert.Equal(0, categories.Single(c => c.Slug == "snacks").ProductCount);

        // El conteo de cada categoría coincide exactamente con los productos visibles listados en /api/products
        foreach (var category in categories)
        {
            var listedInCategory = products.Items.Count(p => p.Category.Slug == category.Slug);
            Assert.Equal(listedInCategory, category.ProductCount);
        }

        Assert.Equal(products.TotalCount, categories.Sum(c => c.ProductCount));
    }

    [PostgresFact]
    public async Task DevCatalogSeeder_es_idempotente_y_consulta_productos_reales_desde_api()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var scope = _factory!.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DevCatalogSeeder>();
            var first = await seeder.SeedAsync(ct);
            Assert.Equal(27, first.Inserted);
            Assert.Equal(0, first.Updated);

            var second = await seeder.SeedAsync(ct);
            Assert.Equal(0, second.Inserted);
            Assert.Equal(27, second.Updated);
        }

        // De los 27 registros dev, 4 están ocultos (baja, precio 0, precio sospechoso, nombre vacío) -> 23 visibles
        var result = await _client!.GetFromJsonAsync<PagedResult<PublicProductDto>>(
            "/api/products?pageSize=50", JsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(23, result.TotalCount);
        Assert.Equal(23, result.Items.Count);
        Assert.Contains(result.Items, p => p.Available);
        Assert.Contains(result.Items, p => !p.Available);
        Assert.Contains(result.Items, p => p.ImageUrl is null);
        Assert.Contains(result.Items, p => p.ImageUrl is not null);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static void AssertNoSensitiveFields(string rawJson)
    {
        Assert.DoesNotContain("COD-SECRETO", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DESCR ORIGINAL ASPEL", rawJson, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(rawJson);
        AssertNoForbiddenKeys(doc.RootElement);
    }

    private static void AssertNoForbiddenKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                Assert.False(
                    prop.Name is "code" or "description" or "stock" or "isActive",
                    $"La respuesta pública expuso el campo interno '{prop.Name}'.");
                AssertNoForbiddenKeys(prop.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertNoForbiddenKeys(item);
            }
        }
    }

    private async Task<SeedIds> SeedCatalogAsync()
    {
        await using var db = _database!.CreateContext();
        var now = DateTime.UtcNow;

        var abarrotes = new Category { Id = Guid.NewGuid(), Name = "Abarrotes", Slug = "abarrotes", Position = 1 };
        var bebidas = new Category { Id = Guid.NewGuid(), Name = "Bebidas", Slug = "bebidas", Position = 2 };
        var lacteos = new Category { Id = Guid.NewGuid(), Name = "Lácteos", Slug = "lacteos", Position = 3 };
        var snacks = new Category { Id = Guid.NewGuid(), Name = "Snacks", Slug = "snacks", Position = 4 };
        db.Categories.AddRange(abarrotes, bebidas, lacteos, snacks);

        var othersId = await db.Categories
            .Where(c => c.Id == Category.OthersId)
            .Select(c => c.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        var pAgua = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-01",
            Description = "DESCR ORIGINAL ASPEL AGUA 1LT",
            DisplayName = "Agua Pura 1 L",
            CategoryId = bebidas.Id,
            PublicPrice = 18.50m,
            Stock = 12m,
            IsActive = true,
            SyncedAt = now
        };

        var pArroz = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-02",
            Description = "DESCR ORIGINAL ASPEL ARROZ",
            DisplayName = "Arroz Blanco 2 lb",
            CategoryId = abarrotes.Id,
            PublicPrice = 32.10m,
            Stock = 0m,
            IsActive = true,
            SyncedAt = now
        };

        var pLeche = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-03",
            Description = "DESCR ORIGINAL ASPEL LECHE",
            DisplayName = "Leche Entera 1 L",
            CategoryId = lacteos.Id,
            PublicPrice = 45.00m,
            Stock = -3m,
            IsActive = true,
            SyncedAt = now
        };

        var pOtros = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-04",
            Description = "DESCR ORIGINAL ASPEL OTROS",
            DisplayName = "Producto Sin Categoría",
            CategoryId = othersId,
            PublicPrice = 75.25m,
            Stock = 5m,
            IsActive = true,
            SyncedAt = now
        };

        // Ocultos según docs/business-rules.md
        var hiddenInactive = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-H1",
            Description = "DESCR ORIGINAL ASPEL BAJA",
            DisplayName = "Producto Dado de Baja",
            CategoryId = abarrotes.Id,
            PublicPrice = 20m,
            Stock = 5m,
            IsActive = false,
            SyncedAt = now
        };

        var hiddenZeroPrice = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-H2",
            Description = "DESCR ORIGINAL ASPEL SIN PRECIO",
            DisplayName = "Producto Sin Precio",
            CategoryId = abarrotes.Id,
            PublicPrice = 0m,
            Stock = 5m,
            IsActive = true,
            SyncedAt = now
        };

        var hiddenSuspiciousPrice = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-H3",
            Description = "DESCR ORIGINAL ASPEL PRECIO SOSPECHOSO",
            DisplayName = "Producto Precio Sospechoso",
            CategoryId = bebidas.Id,
            PublicPrice = 100_000m,
            Stock = 5m,
            IsActive = true,
            SyncedAt = now
        };

        var hiddenEmptyName = new Product
        {
            Id = Guid.NewGuid(),
            Code = "COD-SECRETO-H4",
            Description = "DESCR ORIGINAL ASPEL SIN NOMBRE",
            DisplayName = "   ",
            CategoryId = lacteos.Id,
            PublicPrice = 15m,
            Stock = 5m,
            IsActive = true,
            SyncedAt = now
        };

        db.Products.AddRange(
            pAgua, pArroz, pLeche, pOtros,
            hiddenInactive, hiddenZeroPrice, hiddenSuspiciousPrice, hiddenEmptyName);

        db.ProductImages.Add(new ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = pAgua.Id,
            ImagePath = "/images/agua-pura.jpg",
            MimeType = "image/jpeg",
            FileSizeBytes = 1024
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new SeedIds(
            pAgua.Id,
            pArroz.Id,
            pLeche.Id,
            [hiddenInactive.Id, hiddenZeroPrice.Id, hiddenSuspiciousPrice.Id, hiddenEmptyName.Id]);
    }

    private sealed record SeedIds(
        Guid VisibleWithImageId,
        Guid VisibleZeroStockId,
        Guid VisibleNegativeStockId,
        Guid[] HiddenIds);
}
