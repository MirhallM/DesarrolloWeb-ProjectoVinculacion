namespace backend.Auth;

/// <summary>
/// Comando <c>dotnet backend.dll admin promote &lt;correo&gt;</c> para asignar la cuenta administradora
/// sin exponer endpoints públicos ni manejar contraseñas por consola.
/// </summary>
public static class AdminCommand
{
    public const string Name = "admin";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    /// <summary>Códigos de salida: 0 éxito, 1 fallo de validación/operación, 2 uso inválido.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken ct = default)
    {
        if (args.Length != 3 || args[1] != "promote")
        {
            Console.Error.WriteLine("Uso: dotnet backend.dll admin promote <correo>");
            return 2;
        }

        try
        {
            using var scope = services.CreateScope();
            var adminService = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            var result = await adminService.PromoteToAdminAsync(args[2], ct);

            if (result.Status == AdminOperationStatus.Succeeded)
            {
                Console.WriteLine(result.Message);
                return 0;
            }

            Console.Error.WriteLine(result.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"La operación de administración falló: {ex.Message}");
            return 1;
        }
    }
}
