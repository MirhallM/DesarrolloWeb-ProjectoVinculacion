using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncAgent;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Logging;
using SyncAgent.Publishing;
using SyncAgent.Resilience;
using SyncAgent.State;

// Modos (exactamente uno):
//   --once   Una sincronización completa: lee Aspel, entrega los artículos y guarda el estado en disco.
//            Es lo que ejecuta la tarea programada cada 15 minutos.
//   --test   Una sola lectura de Aspel y un resumen en pantalla. No envía ni guarda nada.
const string OnceMode = "--once";
const string TestMode = "--test";

// La consola de Windows usa por defecto una página de códigos antigua (los acentos salen rotos).
// Se fuerza UTF-8; si no hay consola (p. ej. tarea sin sesión), se ignora.
try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
catch (IOException) { }

if (args.Length != 1 || args[0] is not (OnceMode or TestMode))
{
    Console.Error.WriteLine("Uso: SyncAgent --once | --test");
    Console.Error.WriteLine("  --once   Sincronización completa (la que ejecuta la tarea programada).");
    Console.Error.WriteLine("  --test   Lee Aspel una vez y muestra un resumen (no envía ni guarda nada).");
    return ExitCodes.InvalidConfiguration;
}

var runOnce = args[0] == OnceMode;

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

AgentOptions agentOptions;
try
{
    agentOptions = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();
}
catch (InvalidOperationException)
{
    Console.Error.WriteLine("La sección Agent de la configuración tiene un valor con formato inválido (revise Agent:LogRetentionDays).");
    return ExitCodes.InvalidConfiguration;
}

var configurationErrors = options.Validate().Concat(agentOptions.Validate()).ToList();
if (configurationErrors.Count > 0)
{
    foreach (var error in configurationErrors) Console.Error.WriteLine(error);
    return ExitCodes.InvalidConfiguration;
}

var dataDirectory = agentOptions.ResolveDataDirectory(AppContext.BaseDirectory);

// ---------- Logs: consola de una línea (y archivo en --once), con los secretos ocultos ----------
var redactor = SecretRedactor.FromConnectionString(connectionString);
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new ConsoleLineLoggerProvider(redactor));
if (runOnce)
{
    // En --test no se escribe nada en disco: se ejecuta a mano, a menudo como Administrador, y crearía archivos
    // que la cuenta de la tarea quizá no podría modificar.
    builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(dataDirectory, "logs"), redactor));
}

// ---------- Servicios ----------
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(redactor);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SqlErrorDescriber>();
builder.Services.AddSingleton<IAspelProductReader>(_ => new AspelProductReader(connectionString, options));
builder.Services.AddSingleton(_ => new RetryPolicy(options.Retry));
builder.Services.AddSingleton<AspelProductLoader>();
builder.Services.AddSingleton<ICatalogPublisher, NoOpCatalogPublisher>(); // US-06: aquí se conectará la API
builder.Services.AddSingleton(sp => new SyncStateStore(dataDirectory, sp.GetRequiredService<ILogger<SyncStateStore>>()));
builder.Services.AddSingleton<SyncCycle>();

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
    if (!runOnce)
    {
        var runner = new TestModeRunner(
            host.Services.GetRequiredService<AspelProductLoader>(),
            options,
            ConnectionStringDescriber.Describe(connectionString),
            Console.Out);

        return await runner.RunAsync(cancellation.Token);
    }

    // --once: primero se borran los logs viejos y luego se toma el candado de instancia única.
    LogRetention.Cleanup(Path.Combine(dataDirectory, "logs"), agentOptions.LogRetentionDays, DateTime.Today, logger);

    using var instanceLock = InstanceLock.TryAcquire(dataDirectory);
    if (instanceLock is null)
    {
        logger.LogWarning("Ya hay otra ejecución del agente en curso. Esta se cancela.");
        return ExitCodes.AlreadyRunning;
    }

    logger.LogInformation("Conexión: {Connection}", ConnectionStringDescriber.Describe(connectionString));
    return await host.Services.GetRequiredService<SyncCycle>().RunAsync(cancellation.Token);
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
