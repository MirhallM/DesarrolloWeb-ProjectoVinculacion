using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace SyncAgent.Aspel;

/// <summary>Tiempos medidos durante una lectura (los llena <see cref="IAspelProductReader"/>).</summary>
public sealed class AspelReadMetrics
{
    /// <summary>Tiempo en abrir la conexión.</summary>
    public TimeSpan ConnectDuration { get; set; }

    /// <summary>Tiempo desde que se envía la consulta hasta leer la última fila.</summary>
    public TimeSpan QueryDuration { get; set; }
}

public interface IAspelProductReader
{
    /// <summary>Lee los artículos de Aspel en streaming, una fila a la vez.</summary>
    IAsyncEnumerable<AspelProduct> ReadAsync(AspelReadMetrics? metrics = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ejecuta <see cref="AspelQuery"/> contra SQL Server y entrega los artículos en streaming
/// con un <see cref="SqlDataReader"/> (sin ORM y sin cargar todo el resultado en el lector).
/// </summary>
/// <remarks>
/// Solo lectura: un único SELECT, sin transacciones y sin escribir nada.
/// Quien consume este flujo debe hacerlo rápido (solo acumular): mientras el lector está abierto,
/// SQL Server puede mantener un bloqueo compartido sobre la página que se está leyendo.
/// </remarks>
public sealed class AspelProductReader : IAspelProductReader
{
    private readonly string _connectionString;
    private readonly AspelOptions _options;

    /// <param name="connectionString">Cadena de conexión original (contiene credenciales: no se registra).</param>
    public AspelProductReader(string connectionString, AspelOptions options)
    {
        _options = options;
        _connectionString = BuildConnectionString(connectionString, options);
    }

    /// <summary>
    /// Ajusta la cadena con <see cref="SqlConnectionStringBuilder"/>: nombre de aplicación visible para el
    /// administrador de SQL Server, intención de solo lectura y tiempo de espera de conexión.
    /// </summary>
    internal static string BuildConnectionString(string connectionString, AspelOptions options)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "NettesSyncAgent",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = options.ConnectTimeoutSeconds,
            // El agente abre una conexión por ejecución y termina: no tiene sentido mantener un pool.
            Pooling = false,
        };
        return builder.ConnectionString;
    }

    public async IAsyncEnumerable<AspelProduct> ReadAsync(
        AspelReadMetrics? metrics = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        var connectTimer = Stopwatch.StartNew();
        await connection.OpenAsync(cancellationToken);
        connectTimer.Stop();
        if (metrics is not null) metrics.ConnectDuration = connectTimer.Elapsed;

        await using var command = connection.CreateCommand();
        command.CommandText = AspelQuery.Sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.Parameters.Add(AspelQuery.PriceListParameter, SqlDbType.Int).Value = _options.PriceList;

        var queryTimer = Stopwatch.StartNew();
        // SequentialAccess no aplica (columnas pequeñas); SingleResult declara que solo se espera un resultado.
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            yield return AspelProductMapper.Map(reader);
        }

        queryTimer.Stop();
        if (metrics is not null) metrics.QueryDuration = queryTimer.Elapsed;
    }
}
