using System.Text;
using Microsoft.Extensions.Logging;
using SyncAgent.Diagnostics;

namespace SyncAgent.Logging;

/// <summary>Logger que entrega cada línea ya formateada y redactada a un destino.</summary>
internal sealed class LineLogger(string category, SecretRedactor redactor, Func<DateTime> clock, Action<LogLevel, string> write)
    : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        write(logLevel, LogLineFormatter.Format(clock(), logLevel, category, formatter(state, exception), exception, redactor));
    }
}

/// <summary>Escribe una línea por evento en la consola (errores en la salida de error).</summary>
public sealed class ConsoleLineLoggerProvider(SecretRedactor redactor) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new LineLogger(categoryName, redactor, () => DateTime.Now, (level, line) =>
            (level >= LogLevel.Error ? Console.Error : Console.Out).WriteLine(line));

    public void Dispose() { }
}

/// <summary>
/// Escribe en <c>agent-AAAA-MM-DD.log</c> dentro de la carpeta indicada (un archivo por día).
/// Si no puede escribir, ignora el error: un problema de disco no debe impedir la sincronización.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly SecretRedactor _redactor;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory, SecretRedactor redactor, Func<DateTime>? clock = null)
    {
        _directory = directory;
        _redactor = redactor;
        _clock = clock ?? (() => DateTime.Now);
        try { Directory.CreateDirectory(directory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string FileNameFor(DateTime day) => $"agent-{day:yyyy-MM-dd}.log";

    public ILogger CreateLogger(string categoryName) =>
        new LineLogger(categoryName, _redactor, _clock, (_, line) => Append(line));

    private void Append(string line)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(Path.Combine(_directory, FileNameFor(_clock())), line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin disco o sin permisos: se sigue sin registrar en archivo.
        }
    }

    public void Dispose() { }
}
