using backend.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

/// <summary>
/// Contexto de la base de datos de la aplicación.
/// IdentityDbContext aporta las tablas de usuarios, roles y de apoyo;
/// aquí se agregan las tablas del catálogo.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    // Ids fijos para que la semilla de roles no cambie entre migraciones.
    private static readonly Guid UserRoleId = new("6f1f6c3a-6b3e-4b8e-9a51-1d6c2a7b8e01");
    private static readonly Guid AdminRoleId = new("6f1f6c3a-6b3e-4b8e-9a51-1d6c2a7b8e02");

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Favorite> Favorites => Set<Favorite>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<FeaturedProduct> FeaturedProducts => Set<FeaturedProduct>();

    public DbSet<SiteContent> SiteContent => Set<SiteContent>();

    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Primero Identity: define sus tablas, llaves e índices.
        base.OnModelCreating(builder);

        // Búsqueda por similitud de texto (descripción de productos).
        builder.HasPostgresExtension("pg_trgm");

        ConfigureIdentity(builder);
        ConfigureProducts(builder);
        ConfigureFavorites(builder);
        ConfigureProductImages(builder);
        ConfigureFeaturedProducts(builder);
        ConfigureSiteContent(builder);
        ConfigureSyncRuns(builder);

        // Al final, cuando el modelo ya está completo.
        builder.ApplySnakeCaseNames();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AppUser>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Favorite>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
        }
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        });

        // Roles iniciales. La primera cuenta Admin NO se crea aquí:
        // se asigna con un proceso controlado (ver docs/architecture.md).
        builder.Entity<AppRole>().HasData(
            new AppRole
            {
                Id = UserRoleId,
                Name = AppRoleNames.User,
                NormalizedName = AppRoleNames.User.ToUpperInvariant(),
                ConcurrencyStamp = "b1d3d1f2-0c2a-4f55-9a0e-6a1f0e7c1a01"
            },
            new AppRole
            {
                Id = AdminRoleId,
                Name = AppRoleNames.Admin,
                NormalizedName = AppRoleNames.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "b1d3d1f2-0c2a-4f55-9a0e-6a1f0e7c1a02"
            });
    }

    private static void ConfigureProducts(ModelBuilder builder)
    {
        builder.Entity<Product>(product =>
        {
            product.ToTable("products", t =>
            {
                t.HasCheckConstraint("ck_products_public_price", "public_price >= 0");
            });

            product.HasKey(p => p.Id);

            product.Property(p => p.Code).HasMaxLength(50).IsRequired();
            product.Property(p => p.Description).HasMaxLength(255).IsRequired();
            product.Property(p => p.CategoryCode).HasMaxLength(50);
            product.Property(p => p.CategoryName).HasMaxLength(150);
            product.Property(p => p.PublicPrice).HasPrecision(12, 2);
            product.Property(p => p.Stock).HasPrecision(14, 4);
            product.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            product.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");

            product.HasIndex(p => p.Code).IsUnique();
            product.HasIndex(p => new { p.IsActive, p.CategoryCode });

            // Índice GIN con trigramas para buscar por descripción.
            product.HasIndex(p => p.Description)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");
        });
    }

    private static void ConfigureFavorites(ModelBuilder builder)
    {
        builder.Entity<Favorite>(favorite =>
        {
            favorite.ToTable("favorites");

            // La llave compuesta impide que un usuario repita un favorito.
            favorite.HasKey(f => new { f.UserId, f.ProductId });

            favorite.Property(f => f.CreatedAt).HasDefaultValueSql("now()");

            favorite.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            favorite.HasOne(f => f.Product)
                .WithMany()
                .HasForeignKey(f => f.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureProductImages(ModelBuilder builder)
    {
        builder.Entity<ProductImage>(image =>
        {
            image.ToTable("product_images", t =>
            {
                t.HasCheckConstraint("ck_product_images_file_size_bytes", "file_size_bytes > 0");
            });

            image.HasKey(i => i.Id);

            image.Property(i => i.ImagePath).IsRequired();
            image.Property(i => i.AltText).HasMaxLength(255);
            image.Property(i => i.MimeType).HasMaxLength(100).IsRequired();
            image.Property(i => i.CreatedAt).HasDefaultValueSql("now()");
            image.Property(i => i.UpdatedAt).HasDefaultValueSql("now()");

            // Uno a uno: EF crea un índice único sobre product_id.
            image.HasOne(i => i.Product)
                .WithOne()
                .HasForeignKey<ProductImage>(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            image.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(i => i.UploadedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureFeaturedProducts(ModelBuilder builder)
    {
        builder.Entity<FeaturedProduct>(featured =>
        {
            featured.ToTable("featured_products", t =>
            {
                t.HasCheckConstraint("ck_featured_products_position", "position >= 1");
            });

            featured.HasKey(f => f.Id);

            featured.Property(f => f.CreatedAt).HasDefaultValueSql("now()");
            featured.Property(f => f.UpdatedAt).HasDefaultValueSql("now()");

            featured.HasOne(f => f.Product)
                .WithOne()
                .HasForeignKey<FeaturedProduct>(f => f.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Dos destacados activos no pueden compartir posición.
            featured.HasIndex(f => f.Position)
                .IsUnique()
                .HasFilter("is_active");
        });
    }

    private static void ConfigureSiteContent(ModelBuilder builder)
    {
        builder.Entity<SiteContent>(content =>
        {
            content.ToTable("site_content", t =>
            {
                t.HasCheckConstraint(
                    "ck_site_content_content_type",
                    $"content_type IN ('{SiteContentTypes.Banner}', '{SiteContentTypes.Promotion}')");
                t.HasCheckConstraint(
                    "ck_site_content_dates",
                    "end_at IS NULL OR start_at IS NULL OR end_at > start_at");
            });

            content.HasKey(c => c.Id);

            content.Property(c => c.ContentType).HasMaxLength(50).IsRequired();
            content.Property(c => c.Title).HasMaxLength(200).IsRequired();
            content.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
            content.Property(c => c.UpdatedAt).HasDefaultValueSql("now()");

            content.HasIndex(c => new { c.IsActive, c.ContentType, c.StartAt, c.EndAt });

            content.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(c => c.UpdatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureSyncRuns(ModelBuilder builder)
    {
        builder.Entity<SyncRun>(run =>
        {
            run.ToTable("sync_runs", t =>
            {
                t.HasCheckConstraint(
                    "ck_sync_runs_status",
                    $"status IN ('{SyncRunStatuses.Running}', '{SyncRunStatuses.Succeeded}', '{SyncRunStatuses.Failed}')");
                t.HasCheckConstraint(
                    "ck_sync_runs_finished_at",
                    "finished_at IS NULL OR finished_at >= started_at");
            });

            run.HasKey(r => r.Id);

            run.Property(r => r.Status).HasMaxLength(20).IsRequired();

            // Solo una sincronización en ejecución a la vez.
            run.HasIndex(r => r.Status)
                .IsUnique()
                .HasFilter($"status = '{SyncRunStatuses.Running}'");

            // Para obtener la última sincronización exitosa (US-12).
            run.HasIndex(r => new { r.Status, r.FinishedAt });
        });
    }
}
