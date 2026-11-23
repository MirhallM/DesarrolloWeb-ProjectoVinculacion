using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace backend.Data;

/// <summary>
/// Comando de diagnóstico <c>dotnet backend.dll dp-check protect|unprotect &lt;texto&gt;</c>.
/// Sirve para comprobar que las llaves de Data Protection viven en PostgreSQL: lo protegido en un contenedor
/// debe poder leerse en otro contenedor nuevo contra la misma base (ver docs/deployment.md).
/// </summary>
/// <remarks>
/// Usa un propósito propio, así que no puede leer ni fabricar cookies de Identity ni otros datos de la aplicación.
/// </remarks>
public static class DataProtectionCheckCommand
{
    public const string Name = "dp-check";

    public const string Purpose = "nettes.dp-check";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    /// <summary>Devuelve el código de salida: 0 si terminó bien, 1 si no pudo, 2 si el uso es incorrecto.</summary>
    public static int Run(IServiceProvider services, string[] args)
    {
        if (args.Length != 3 || args[1] is not ("protect" or "unprotect"))
        {
            Console.Error.WriteLine("Uso: dotnet backend.dll dp-check protect <texto> | unprotect <texto protegido>");
            return 2;
        }

        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

        try
        {
            Console.WriteLine(args[1] == "protect" ? protector.Protect(args[2]) : protector.Unprotect(args[2]));
            return 0;
        }
        catch (CryptographicException ex)
        {
            Console.Error.WriteLine($"No se pudo desproteger con las llaves de esta base: {ex.Message}");
            return 1;
        }
    }
}
