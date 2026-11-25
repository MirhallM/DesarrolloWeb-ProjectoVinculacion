namespace SyncAgent.Resilience;

/// <summary>Reintentos ante fallos transitorios. Lo usan la lectura de Aspel (<c>Aspel:Retry</c>) y el envío a la API.</summary>
public sealed class RetryOptions
{
    /// <summary>Intentos totales (el primero más los reintentos). 4 = 1 intento + 3 reintentos con las esperas por defecto.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>
    /// Espera antes de cada reintento, en segundos (de fábrica 5, 15 y 45). Si hay menos valores que reintentos,
    /// se repite el último. Si <c>appsettings.Production.json</c> la define, reemplaza a la de fábrica por completo.
    /// </summary>
    public int[] DelaysSeconds { get; set; } = [5, 15, 45];
}
