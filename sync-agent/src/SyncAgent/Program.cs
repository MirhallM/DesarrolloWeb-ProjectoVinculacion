using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncAgent;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Logging;
using SyncAgent.Resilience;

// Modos:
//   --test   Una sola lectura de Aspel y un resumen en pantalla. No envía ni guarda nada.
const string TestMode = "--test";

// La consola de Windows usa por defecto una página de códigos antigua (los acentos salen rotos).
// Se fuerza UTF-8; si no hay consola (p. ej. tarea sin sesión), se ignora.
try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
catch (IOException) { }

if (args.Length != 1 || args[0] != TestMode)
{
    Console.Error.WriteLine("Uso: SyncAgent --test");
    Console.Error.WriteLine("  --test   Lee Aspel una vez y muestra un resumen (no envía nada).");
    return ExitCodes.InvalidConfiguration;
}

// Los argumentos NO se pasan al host: "--test" se interpretaría como una clave de configuración.
// ContentRootPath = carpeta del exe: el Programador de tareas arranca el proceso en System32.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
});

// ---------- Configuración ----------
var connectionString = builder.Configuration.GetConnectionString("AspelSae");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Falta la cadena de conexión ConnectionStrings:AspelSae. " +
        "En desarrollo use User Secrets; en la tienda, appsettings.Production.json (ver sync-agent/README.md).");
    return ExitCodes.InvalidConfiguration;
}

try
{
    _ = new SqlConnectionStringBuilder(connectionString);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    // No se muestra el mensaje de la excepción: podría repetir parte de la cadena.
    Console.Error.WriteLine("La cadena de conexión ConnectionStrings:AspelSae tiene un formato inválido.");
    return ExitCodes.InvalidConfiguration;
}

var options = AspelOptions.Load(builder.Configuration);
var configurationErrors = options.Validate();
if (configurationErrors.Count > 0)
{
    foreach (var error in configurationErrors) Console.Error.WriteLine(error);
    return ExitCodes.InvalidConfiguration;
}

// ---------- Logs: consola de una línea, con los secretos ocultos ----------
var redactor = SecretRedactor.FromConnectionString(connectionString);
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new ConsoleLineLoggerProvider(redactor));

// ---------- Servicios ----------
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(redactor);
builder.Services.AddSingleton<SqlErrorDescriber>();
builder.Services.AddSingleton<IAspelProductReader>(_ => new AspelProductReader(connectionString, options));
builder.Services.AddSingleton(_ => new RetryPolicy(options.Retry));
builder.Services.AddSingleton<AspelProductLoader>();

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // cierre ordenado en vez de matar el proceso
    cancellation.Cancel();
};

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SyncAgent");
try
{
    var runner = new TestModeRunner(
        host.Services.GetRequiredService<AspelProductLoader>(),
        options,
        ConnectionStringDescriber.Describe(connectionString),
        Console.Out);

    return await runner.RunAsync(cancellation.Token);
}
catch (OperationCanceledException)
{
    logger.LogWarning("Ejecución cancelada.");
    return ExitCodes.UnexpectedError;
}
catch (Exception ex)
{
    // El formateador de logs oculta usuario y contraseña si aparecieran en el texto.
    logger.LogError(ex, "Error inesperado.");
    return ExitCodes.UnexpectedError;
}
