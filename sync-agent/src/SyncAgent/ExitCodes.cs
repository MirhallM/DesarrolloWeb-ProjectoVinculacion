namespace SyncAgent;

/// <summary>Códigos de salida del programa (los ve el Programador de tareas en "Último resultado").</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidConfiguration = 1;
    public const int SqlFailure = 2;
    public const int AlreadyRunning = 3;
    public const int UnexpectedError = 4;

    /// <summary>No se pudo enviar el catálogo a la API (sin conexión, llave rechazada, freno de seguridad…).</summary>
    public const int ApiFailure = 5;
}
