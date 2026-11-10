using backend.Catalog;

namespace backend.Data.Seed;

/// <summary>
/// Comando <c>dotnet backend.dll seed</c>: siembra categorías y reglas desde categories.json y termina.
/// Pensado para el pre-deploy de Railway, después de aplicar las migraciones.
/// </summary>
public static class CatalogSeedCommand
{
    public const string Name = "seed";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    /// <summary>Devuelve el código de salida: 0 si terminó bien, 1 si falló.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        try
        {
            using var scope = services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<CatalogSeeder>();
            var rules = scope.ServiceProvider.GetRequiredService<CategoryRuleSet>();

            var report = await seeder.SeedAsync(rules, ct);

            Console.WriteLine(report);
            foreach (var warning in report.Warnings)
            {
                Console.WriteLine($"ADVERTENCIA: {warning}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"La siembra del catálogo falló: {ex.Message}");
            return 1;
        }
    }
}
