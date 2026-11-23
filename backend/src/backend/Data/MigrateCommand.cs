using Microsoft.EntityFrameworkCore;

namespace backend.Data;

/// <summary>
/// Comando <c>dotnet backend.dll migrate</c>: aplica las migraciones pendientes de EF Core y termina.
/// Pensado para el pre-deploy de Railway, antes de <c>seed</c>: si falla, el despliegue se detiene y la
/// versión anterior sigue en línea.
/// </summary>
public static class MigrateCommand
{
    public const string Name = "migrate";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    /// <summary>Devuelve el código de salida: 0 si terminó bien, 1 si falló.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        try
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pending = (await context.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count == 0)
            {
                Console.WriteLine("Migraciones: la base ya está al día.");
                return 0;
            }

            Console.WriteLine($"Migraciones pendientes ({pending.Count}): {string.Join(", ", pending)}");
            await context.Database.MigrateAsync(ct);
            Console.WriteLine("Migraciones aplicadas.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"La migración falló: {ex.Message}");
            return 1;
        }
    }
}
