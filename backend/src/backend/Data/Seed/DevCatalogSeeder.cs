using backend.Catalog;
using backend.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Seed;

/// <summary>Resumen de la siembra de productos de prueba para desarrollo local.</summary>
public sealed record DevCatalogSeedReport(int Inserted, int Updated);

/// <summary>
/// Carga un conjunto idempotente de productos de prueba (prefijo DEV-) en PostgreSQL
/// usando las reglas reales de categorización y generación de nombre para mostrar.
/// Solo debe ejecutarse en el entorno Development mientras no esté conectado el agente US-06.
/// </summary>
public sealed class DevCatalogSeeder(
    AppDbContext db,
    CatalogSeeder catalogSeeder,
    CategoryRuleSet ruleSet,
    ICategorizer categorizer,
    IDisplayNamer displayNamer,
    ILogger<DevCatalogSeeder> logger)
{
    private sealed record SampleProduct(
        string Code,
        string Description,
        decimal PublicPrice,
        decimal Stock,
        bool IsActive = true,
        bool ForceEmptyDisplayName = false,
        string? ImagePath = null);

    private static readonly SampleProduct[] Samples =
    [
        // Productos visibles y disponibles en distintas categorías reales de categories.json
        new("DEV-001", "LECHE ENT LEYDE 1LT", 38.50m, 24m, ImagePath: "https://images.unsplash.com/photo-1550583724-b2692b85b150?auto=format&fit=crop&w=800&q=80"),
        new("DEV-002", "SOPA MAGGI POLLO FIDEOS 50G", 14.00m, 40m),
        new("DEV-003", "ARROZ BLANCO 2LB", 34.00m, 18.5m),
        new("DEV-004", "BANANO MADURO POR LB", 12.50m, 35m, ImagePath: "https://images.unsplash.com/photo-1571771894821-ce9b6c11b08e?auto=format&fit=crop&w=800&q=80"),
        new("DEV-005", "AGUACATE HASS UNIDAD", 25.00m, 15m, ImagePath: "https://images.unsplash.com/photo-1519162808019-7de1683fa2ad?auto=format&fit=crop&w=800&q=80"),
        new("DEV-006", "HUEVOS DE GRANJA DOCENA", 62.00m, 12m, ImagePath: "https://images.unsplash.com/photo-1506976785307-8732e854ad03?auto=format&fit=crop&w=800&q=80"),
        new("DEV-007", "PAN BLANCO REBANADO 680G", 54.00m, 9m, ImagePath: "https://images.unsplash.com/photo-1598373182133-52452f7691ef?auto=format&fit=crop&w=800&q=80"),
        new("DEV-008", "JUGO DE NARANJA NATURAL 1LT", 75.00m, 14m, ImagePath: "https://images.unsplash.com/photo-1613478223719-2ab802602423?auto=format&fit=crop&w=800&q=80"),
        new("DEV-009", "CAFE MOLIDO HONDURENO 500G", 180.00m, 20m, ImagePath: "https://images.unsplash.com/photo-1524350876685-274059332603?auto=format&fit=crop&w=800&q=80"),
        new("DEV-010", "GALLETA CHOKIS CHOCOBASE", 22.00m, 30m),
        new("DEV-011", "SUAVITEL TURQUESA 740ML", 68.00m, 10m),
        new("DEV-012", "LISTERINE FRESHBURST 500ML", 145.00m, 7m),
        new("DEV-013", "PANAL HUGGIES CLASICO JUMBO", 320.00m, 6m),
        new("DEV-014", "ALIMENTO PERRO DOGUI VEGETAL", 95.00m, 11m),
        new("DEV-015", "MARTILLO CARPINTERO 16OZ", 210.00m, 4m),
        new("DEV-016", "CABLE USB UNNO V8", 110.00m, 8m),
        new("DEV-017", "CUADERNO ESPIRAL 100 HOJAS", 42.00m, 22m),
        new("DEV-018", "GLOBO PLATEADO M40", 35.00m, 16m),
        new("DEV-019", "RECARGAS NORMALES CLARO", 50.00m, 99m),
        new("DEV-020", "ARTICULO SIN REGLA CONOCIDA XYZ", 49.99m, 5m), // Cae en "Otros"

        // Productos visibles pero AGOTADOS (Stock <= 0)
        new("DEV-021", "YOGURT FRESA SULITA 100ML", 16.00m, 0m),
        new("DEV-022", "DETERGENTE EN POLVO 1KG", 72.50m, -2m),
        new("DEV-023", "MANZANA ROJA GALA POR LB", 48.00m, 0m, ImagePath: "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?auto=format&fit=crop&w=800&q=80"),

        // Productos ocultos según docs/business-rules.md (no deben aparecer en /api/products y responden 404 en detalle)
        new("DEV-H01", "PRODUCTO DADO DE BAJA", 30.00m, 5m, IsActive: false),
        new("DEV-H02", "PRODUCTO SIN PRECIO EN LISTA", 0m, 5m),
        new("DEV-H03", "PRODUCTO PRECIO SOSPECHOSO", 125_000m, 5m),
        new("DEV-H04", "NULL", 25.00m, 5m, ForceEmptyDisplayName: true),
    ];

    public async Task<DevCatalogSeedReport> SeedAsync(CancellationToken ct = default)
    {
        await catalogSeeder.SeedAsync(ruleSet, ct);

        var categoriesBySlug = await db.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Slug, c => c.Id, StringComparer.Ordinal, ct);

        var devCodes = Samples.Select(s => s.Code).ToArray();
        var existingByCode = await db.Products
            .Where(p => devCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct);

        var existingIds = existingByCode.Values.Select(p => p.Id).ToArray();
        var imagesByProductId = await db.ProductImages
            .Where(img => existingIds.Contains(img.ProductId))
            .ToDictionaryAsync(img => img.ProductId, ct);

        var now = DateTime.UtcNow;
        int inserted = 0, updated = 0;

        foreach (var sample in Samples)
        {
            var match = categorizer.Categorize(sample.Description);
            var categoryId = categoriesBySlug.TryGetValue(match.Slug, out var id)
                ? id
                : Category.OthersId;

            var displayName = sample.ForceEmptyDisplayName
                ? string.Empty
                : displayNamer.GetDisplayName(sample.Description);

            if (!existingByCode.TryGetValue(sample.Code, out var product))
            {
                product = new Product
                {
                    Id = Guid.NewGuid(),
                    Code = sample.Code,
                    Description = sample.Description,
                    DisplayName = displayName,
                    CategoryId = categoryId,
                    PublicPrice = sample.PublicPrice,
                    Stock = sample.Stock,
                    IsActive = sample.IsActive,
                    SyncedAt = now,
                };
                db.Products.Add(product);
                inserted++;
            }
            else
            {
                product.Description = sample.Description;
                product.DisplayName = displayName;
                product.CategoryId = categoryId;
                product.PublicPrice = sample.PublicPrice;
                product.Stock = sample.Stock;
                product.IsActive = sample.IsActive;
                product.SyncedAt = now;
                updated++;
            }

            SyncSampleImage(product, sample.ImagePath, imagesByProductId);
        }

        await db.SaveChangesAsync(ct);

        var report = new DevCatalogSeedReport(inserted, updated);
        logger.LogInformation("Siembra de datos de prueba (dev): {Report}", report);
        return report;
    }

    private void SyncSampleImage(
        Product product,
        string? imagePath,
        Dictionary<Guid, ProductImage> imagesByProductId)
    {
        imagesByProductId.TryGetValue(product.Id, out var existingImage);

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            if (existingImage is not null)
            {
                db.ProductImages.Remove(existingImage);
            }
            return;
        }

        if (existingImage is null)
        {
            db.ProductImages.Add(new ProductImage
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ImagePath = imagePath,
                AltText = product.DisplayName,
                MimeType = "image/jpeg",
                FileSizeBytes = 1024,
            });
        }
        else
        {
            existingImage.ImagePath = imagePath;
            existingImage.AltText = product.DisplayName;
        }
    }
}
