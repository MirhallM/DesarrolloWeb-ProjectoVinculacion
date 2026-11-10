using SyncAgent.Aspel;

namespace SyncAgent.Resilience;

/// <summary>
/// Repite una operación con espera entre intentos cuando falla por un motivo transitorio.
/// Los errores no transitorios y la cancelación se propagan de inmediato.
/// </summary>
public sealed class RetryPolicy
{
    private readonly AspelOptions.RetryOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">Permite sustituir la espera en las pruebas; por defecto es <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public RetryPolicy(AspelOptions.RetryOptions options, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _options = options;
        _delay = delay ?? Task.Delay;
    }

    /// <param name="operation">Recibe el número de intento (desde 1).</param>
    /// <param name="onRetry">Se invoca antes de esperar: intento que falló, espera y excepción.</param>
    public async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> operation,
        Func<Exception, bool> isTransient,
        Action<int, TimeSpan, Exception>? onRetry,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(attempt, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                                       && attempt < _options.MaxAttempts
                                       && isTransient(ex))
            {
                var wait = DelayBefore(attempt);
                onRetry?.Invoke(attempt, wait, ex);
                await _delay(wait, cancellationToken);
            }
        }
    }

    /// <summary>Espera después del intento <paramref name="failedAttempt"/>; repite el último valor si faltan.</summary>
    internal TimeSpan DelayBefore(int failedAttempt)
    {
        var delays = _options.DelaysSeconds;
        if (delays.Length == 0) return TimeSpan.Zero;
        return TimeSpan.FromSeconds(delays[Math.Min(failedAttempt - 1, delays.Length - 1)]);
    }
}
