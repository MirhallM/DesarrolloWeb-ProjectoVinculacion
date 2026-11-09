using Microsoft.Extensions.Logging;
using SyncAgent.Diagnostics;
using SyncAgent.Resilience;

namespace SyncAgent.Aspel;

/// <summary>Resultado de una lectura completa.</summary>
/// <param name="Attempts">Cuántos intentos hicieron falta (1 = sin reintentos).</param>
public sealed record AspelLoadResult(IReadOnlyList<AspelProduct> Products, AspelReadMetrics Metrics, int Attempts);

/// <summary>
/// Falla de lectura de Aspel con un mensaje ya saneado (sin usuario ni contraseña).
/// A propósito NO guarda la excepción original: el mensaje de SqlClient puede incluir el usuario.
/// </summary>
public sealed class AspelReadException(string safeMessage, int? sqlErrorNumber)
    : Exception(safeMessage)
{
    public int? SqlErrorNumber { get; } = sqlErrorNumber;
}

/// <summary>
/// Lee todos los artículos con reintentos ante fallos transitorios. Cada intento empieza de cero:
/// si la conexión se corta a mitad de la lectura se descarta lo parcial y se vuelve a leer completo.
/// </summary>
public sealed class AspelProductLoader(
    IAspelProductReader reader,
    RetryPolicy retryPolicy,
    SqlErrorDescriber errorDescriber,
    ILogger<AspelProductLoader> logger)
{
    public async Task<AspelLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await retryPolicy.ExecuteAsync(
                async (attempt, token) =>
                {
                    var metrics = new AspelReadMetrics();
                    var products = new List<AspelProduct>(20_000);

                    // Solo acumular: nada pesado por fila mientras el lector sigue abierto.
                    await foreach (var product in reader.ReadAsync(metrics, token))
                    {
                        products.Add(product);
                    }

                    return new AspelLoadResult(products, metrics, attempt);
                },
                SqlTransientErrors.IsTransient,
                (attempt, wait, ex) => logger.LogWarning(
                    "Intento {Attempt} falló. Se reintenta en {Seconds} s. {Error}",
                    attempt, wait.TotalSeconds, errorDescriber.Describe(ex)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Se reemplaza por una excepción saneada: ni el logger ni el estado en disco ven la original.
            throw new AspelReadException(errorDescriber.Describe(ex), SqlTransientErrors.GetNumber(ex));
        }
    }
}
