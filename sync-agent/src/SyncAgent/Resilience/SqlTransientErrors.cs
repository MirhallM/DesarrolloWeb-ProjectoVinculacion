using Microsoft.Data.SqlClient;

namespace SyncAgent.Resilience;

/// <summary>Decide qué fallos vale la pena reintentar (red, tiempos de espera, bloqueos) y cuáles no.</summary>
public static class SqlTransientErrors
{
    // Errores que suelen resolverse solos si se espera un poco.
    private static readonly HashSet<int> TransientNumbers =
    [
        -2,     // tiempo de espera agotado
        53,     // no se encontró la ruta de red
        40,     // no se pudo abrir la conexión con el servidor
        121,    // el tiempo del semáforo expiró
        233,    // el otro extremo cerró la conexión
        258,    // se agotó el tiempo de espera de la conexión (TCP sin respuesta)
        1205,   // víctima de deadlock
        4060,   // no se pudo abrir la base (p. ej. aún está recuperándose tras un reinicio)
        10053,  // conexión abortada
        10054,  // conexión restablecida por el servidor
        10060,  // tiempo de espera de la conexión TCP
        10061,  // conexión rechazada (SQL Server aún no escucha)
        11001,  // host desconocido (red aún no lista)
    ];

    // NO reintentar: 18456 (login fallido) repetido solo bloquearía la cuenta; 229/262 (permiso denegado),
    // 207/208 (esquema distinto) no se arreglan esperando.

    public static bool IsTransientNumber(int number) => TransientNumbers.Contains(number);

    public static bool IsTransient(Exception exception) => exception switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(e => IsTransientNumber(e.Number)) || IsTransientNumber(sql.Number),
        TimeoutException => true,
        _ => false,
    };

    /// <summary>Número del primer error de SQL Server de la excepción, o null si no es una <see cref="SqlException"/>.</summary>
    public static int? GetNumber(Exception exception) => exception is SqlException sql ? sql.Number : null;
}
