using Microsoft.Extensions.Logging;
using SyncAgent.Diagnostics;

namespace SyncAgent.Logging;

/// <summary>Da formato a una línea de log y le oculta los secretos conocidos.</summary>
public static class LogLineFormatter
{
    public static string Format(
        DateTime timestamp, LogLevel level, string category, string message, Exception? exception, SecretRedactor redactor)
    {
        var line = $"{timestamp:yyyy-MM-dd HH:mm:ss} [{Abbreviate(level)}] {ShortCategory(category)}: {message}";
        if (exception is not null)
        {
            // ToString() incluye el tipo, el mensaje y la traza; el redactor oculta usuario/contraseña si aparecen.
            line += Environment.NewLine + exception;
        }

        return redactor.Redact(line);
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }
}
