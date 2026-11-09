namespace SyncAgent;

/// <summary>
/// Impide que dos ejecuciones del agente se solapen (por ejemplo, si una tarda más que el intervalo de 15 minutos).
/// </summary>
/// <remarks>
/// Es un archivo (<c>agent.lock</c>) abierto con acceso exclusivo mientras dure la ejecución. A diferencia de un
/// mutex global no necesita privilegios especiales, vale por carpeta de instalación y el sistema operativo lo
/// libera solo si el proceso muere de golpe.
/// </remarks>
public sealed class InstanceLock : IDisposable
{
    public const string FileName = "agent.lock";

    private readonly FileStream _stream;

    private InstanceLock(FileStream stream) => _stream = stream;

    /// <summary>El candado, o null si otra ejecución ya lo tiene.</summary>
    public static InstanceLock? TryAcquire(string directory)
    {
        Directory.CreateDirectory(directory);

        try
        {
            var stream = new FileStream(
                Path.Combine(directory, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new InstanceLock(stream);
        }
        catch (IOException)
        {
            // Violación de uso compartido: otra ejecución tiene el archivo abierto.
            // (Un problema de permisos lanzaría UnauthorizedAccessException y se informa como error, no como "ya hay otra".)
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
