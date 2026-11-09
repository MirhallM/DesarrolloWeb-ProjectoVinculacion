using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SyncAgent.State;

namespace SyncAgent.Tests.Unit;

/// <summary>Logger que guarda los mensajes para poder comprobarlos.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}

public sealed class SyncStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "syncagent-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SyncStateStore Store(ILogger<SyncStateStore>? logger = null) =>
        new(_dir, logger ?? NullLogger<SyncStateStore>.Instance);

    [Fact]
    public void Guarda_y_lee_todos_los_campos()
    {
        var started = new DateTimeOffset(2026, 11, 9, 14, 0, 0, TimeSpan.Zero);
        var state = new SyncState
        {
            LastRunStartedAt = started,
            LastRunFinishedAt = started.AddSeconds(3),
            LastResult = SyncResults.Failed,
            LastError = "falló algo",
            LastSuccessAt = started.AddMinutes(-15),
            ConsecutiveFailures = 2,
            ProductsRead = 18460,
            QueryDurationMs = 850,
            PeakWorkingSetMb = 41.5,
        };

        Store().Save(state);
        var loaded = Store().Load();

        Assert.Equal(started, loaded.LastRunStartedAt);
        Assert.Equal(started.AddSeconds(3), loaded.LastRunFinishedAt);
        Assert.Equal(SyncResults.Failed, loaded.LastResult);
        Assert.Equal("falló algo", loaded.LastError);
        Assert.Equal(started.AddMinutes(-15), loaded.LastSuccessAt);
        Assert.Equal(2, loaded.ConsecutiveFailures);
        Assert.Equal(18460, loaded.ProductsRead);
        Assert.Equal(850, loaded.QueryDurationMs);
        Assert.Equal(41.5, loaded.PeakWorkingSetMb);
    }

    [Fact]
    public void Archivo_inexistente_da_un_estado_vacio()
    {
        var state = Store().Load();

        Assert.Null(state.LastSuccessAt);
        Assert.Null(state.LastResult);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact]
    public void Archivo_corrupto_da_un_estado_vacio_y_una_advertencia_sin_repetir_el_contenido()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SyncStateStore.FileName), "{ esto no es json SECRETO-123");
        var logger = new ListLogger<SyncStateStore>();

        var state = Store(logger).Load();

        Assert.Null(state.LastSuccessAt);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.DoesNotContain("SECRETO-123", warning.Message);
    }

    [Fact]
    public void Crea_la_carpeta_si_no_existe_y_no_deja_archivo_temporal()
    {
        Assert.False(Directory.Exists(_dir));

        Store().Save(new SyncState { ProductsRead = 1 });
        Store().Save(new SyncState { ProductsRead = 2 }); // segunda escritura: reemplaza a la primera

        Assert.True(File.Exists(Path.Combine(_dir, SyncStateStore.FileName)));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal(2, Store().Load().ProductsRead);
    }

    [Fact]
    public void El_json_usa_nombres_en_camelCase()
    {
        Store().Save(new SyncState { LastSuccessAt = DateTimeOffset.UtcNow });

        var text = File.ReadAllText(Path.Combine(_dir, SyncStateStore.FileName));
        Assert.Contains("\"lastSuccessAt\"", text);
        Assert.Contains("\"consecutiveFailures\"", text);
    }
}
