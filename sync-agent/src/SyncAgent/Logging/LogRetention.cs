using System.Globalization;
using Microsoft.Extensions.Logging;

namespace SyncAgent.Logging;

/// <summary>
/// Borra los logs viejos para no llenar el disco del servidor de la tienda.
/// Se decide por la fecha del nombre del archivo (<c>agent-AAAA-MM-DD.log</c>), no por la fecha de modificación.
/// </summary>
public static class LogRetention
{
    /// <summary>
    /// Elimina los <c>agent-*.log</c> con más de <paramref name="retentionDays"/> días de antigüedad
    /// (con 14: el de hace 14 días se conserva y el de hace 15 se borra).
    /// Devuelve cuántos archivos borró. Un archivo que no se pueda borrar solo genera una advertencia.
    /// </summary>
    public static int Cleanup(string directory, int retentionDays, DateTime today, ILogger? logger = null)
    {
        if (!Directory.Exists(directory)) return 0;

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "agent-*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(path)["agent-".Length..];
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue; // no es un log del agente: no se toca

            if ((today.Date - day.Date).TotalDays <= retentionDays) continue;

            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning("No se pudo borrar el log antiguo {File}: {Reason}", Path.GetFileName(path), ex.GetType().Name);
            }
        }

        return deleted;
    }
}
