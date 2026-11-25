namespace SyncAgent.Publishing;

/// <summary>
/// Fallo del envío a la API con un mensaje ya seguro para el log y el estado: nunca incluye la llave.
/// A propósito NO guarda la excepción original.
/// </summary>
public sealed class ApiPublishException(string safeMessage, bool isTransient, int? statusCode = null, string? problemType = null)
    : Exception(safeMessage)
{
    /// <summary>Vale la pena reintentar (red, tiempo de espera, 5xx, 429).</summary>
    public bool IsTransient { get; } = isTransient;

    public int? StatusCode { get; } = statusCode;

    /// <summary><c>type</c> del ProblemDetails de la API (p. ej. <c>sync-safety-brake</c>).</summary>
    public string? ProblemType { get; } = problemType;

    public static bool IsTransientError(Exception ex) => ex is ApiPublishException { IsTransient: true };
}
