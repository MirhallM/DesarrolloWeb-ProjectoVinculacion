namespace backend.Data.Seed;

/// <summary>
/// Comando <c>dotnet run -- seed-dev</c>: carga productos de prueba en PostgreSQL.
/// Solo permitido en el entorno Development para evitar poblar datos falsos en producción.
/// </summary>
public static class DevCatalogSeedCommand
{
    public const string Name = "seed-dev";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine(
                $"El comando '{Name}' solo puede ejecutarse en entorno Development (actual: {environment.EnvironmentName}).");
            return 1;
        }

        try
        {
            using var scope = services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<DevCatalogSeeder>();
            var report = await seeder.SeedAsync(ct);

            Console.WriteLine($"Productos de prueba (dev): +{report.Inserted} insertados, ~{report.Updated} actualizados.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"La siembra de datos de prueba falló: {ex.Message}");
            return 1;
        }
    }
}
