using Microsoft.Extensions.Logging.Abstractions;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Publishing;
using SyncAgent.Resilience;
using SyncAgent.State;

namespace SyncAgent.Tests.Unit;

public sealed class SyncCycleTests : IDisposable
{
    private const string Password = "Cl4ve-Secreta";
    private const string User = "catalogo_lectura";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "syncagent-cycle-" + Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 9, 15, 0, 0, TimeSpan.Zero));
    private readonly RecordingPublisher _publisher = new();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---------- Dobles de prueba ----------

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingPublisher : ICatalogPublisher
    {
        public int Calls { get; private set; }
        public int LastCount { get; private set; }
        public Exception? ThrowOnPublish { get; set; }

        public Task PublishAsync(IReadOnlyList<AspelProduct> products, CancellationToken cancellationToken)
        {
            Calls++;
            LastCount = products.Count;
            return ThrowOnPublish is null ? Task.CompletedTask : throw ThrowOnPublish;
        }
    }

    private sealed class StubReader(Func<IAsyncEnumerable<AspelProduct>> source) : IAspelProductReader
    {
        public IAsyncEnumerable<AspelProduct> ReadAsync(AspelReadMetrics? metrics = null, CancellationToken cancellationToken = default)
        {
            if (metrics is not null) metrics.QueryDuration = TimeSpan.FromMilliseconds(250);
            return source();
        }
    }

    private static async IAsyncEnumerable<AspelProduct> Rows(params AspelProduct[] products)
    {
        foreach (var p in products)
        {
            await Task.Yield();
            yield return p;
        }
    }

    private static async IAsyncEnumerable<AspelProduct> Fails(Exception ex)
    {
        await Task.Yield();
        if (ex is not null) throw ex;
        yield break;
    }

    private static AspelProduct P(string code, string status = "A", double? price = 10) => new(code, "X", 1, status, price);

    private SyncStateStore Store() => new(_dir, NullLogger<SyncStateStore>.Instance);

    private SyncCycle Cycle(IAspelProductReader reader)
    {
        var redactor = new SecretRedactor([Password, User]);
        var loader = new AspelProductLoader(
            reader,
            new RetryPolicy(new AspelOptions.RetryOptions { MaxAttempts = 2, DelaysSeconds = [0] }, (_, _) => Task.CompletedTask),
            new SqlErrorDescriber(redactor),
            NullLogger<AspelProductLoader>.Instance);

        return new SyncCycle(loader, _publisher, Store(), redactor, _clock, NullLogger<SyncCycle>.Instance);
    }

    // ---------- Pruebas ----------

    [Fact]
    public async Task Sincronizacion_correcta_guarda_el_estado_y_entrega_los_articulos_al_publicador()
    {
        var cycle = Cycle(new StubReader(() => Rows(P("A"), P("B", status: "B"), P("C", price: null))));

        var exit = await cycle.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(1, _publisher.Calls);
        Assert.Equal(3, _publisher.LastCount);

        var state = Store().Load();
        Assert.Equal(SyncResults.Succeeded, state.LastResult);
        Assert.Null(state.LastError);
        Assert.Equal(_clock.Now, state.LastSuccessAt);
        Assert.Equal(_clock.Now, state.LastRunStartedAt);
        Assert.Equal(_clock.Now, state.LastRunFinishedAt);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.Equal(3, state.ProductsRead);
        Assert.Equal(250, state.QueryDurationMs);
        Assert.True(state.PeakWorkingSetMb > 0);
    }

    [Fact]
    public async Task Un_fallo_suma_el_contador_y_conserva_la_ultima_sincronizacion_exitosa()
    {
        // 1) una ejecución correcta a las 15:00
        await Cycle(new StubReader(() => Rows(P("A")))).RunAsync(TestContext.Current.CancellationToken);
        var lastSuccess = _clock.Now;

        // 2) dos ejecuciones fallidas, 15 y 30 minutos después
        var failing = Cycle(new StubReader(() => Fails(new InvalidOperationException("sin conexión"))));
        _clock.Now = lastSuccess.AddMinutes(15);
        var first = await failing.RunAsync(TestContext.Current.CancellationToken);
        _clock.Now = lastSuccess.AddMinutes(30);
        var second = await failing.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.SqlFailure, first);
        Assert.Equal(ExitCodes.SqlFailure, second);
        Assert.Equal(1, _publisher.Calls); // el publicador solo se llamó en la ejecución correcta

        var state = Store().Load();
        Assert.Equal(SyncResults.Failed, state.LastResult);
        Assert.Equal(2, state.ConsecutiveFailures);
        Assert.Equal(lastSuccess, state.LastSuccessAt);          // NO se pierde la última vez que sí se sincronizó
        Assert.Equal(lastSuccess.AddMinutes(30), state.LastRunFinishedAt);
        Assert.Contains("sin conexión", state.LastError);
        Assert.Equal(1, state.ProductsRead);                      // el dato de la última lectura buena se conserva
    }

    [Fact]
    public async Task Una_ejecucion_correcta_despues_de_fallos_reinicia_el_contador()
    {
        Store().Save(new SyncState { ConsecutiveFailures = 3, LastResult = SyncResults.Failed, LastError = "viejo" });

        await Cycle(new StubReader(() => Rows(P("A")))).RunAsync(TestContext.Current.CancellationToken);

        var state = Store().Load();
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.Null(state.LastError);
        Assert.Equal(SyncResults.Succeeded, state.LastResult);
    }

    [Fact]
    public async Task El_error_guardado_en_disco_no_contiene_usuario_ni_contrasena()
    {
        var cycle = Cycle(new StubReader(() =>
            Fails(new InvalidOperationException($"Login failed for user '{User}'. Password: {Password}"))));

        await cycle.RunAsync(TestContext.Current.CancellationToken);

        var json = File.ReadAllText(Path.Combine(_dir, SyncStateStore.FileName));
        Assert.DoesNotContain(User, json);
        Assert.DoesNotContain(Password, json);
        Assert.Contains("***", json);
    }

    [Fact]
    public async Task Un_fallo_inesperado_del_publicador_queda_registrado_y_se_propaga()
    {
        _publisher.ThrowOnPublish = new InvalidOperationException($"falla del envío con {Password}");
        var cycle = Cycle(new StubReader(() => Rows(P("A"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cycle.RunAsync(TestContext.Current.CancellationToken));

        var state = Store().Load();
        Assert.Equal(SyncResults.Failed, state.LastResult);
        Assert.Equal(1, state.ConsecutiveFailures);
        Assert.Null(state.LastSuccessAt);
        Assert.DoesNotContain(Password, state.LastError);
    }

    [Fact]
    public async Task La_cancelacion_se_propaga_y_no_se_cuenta_como_fallo()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cycle = Cycle(new StubReader(() => Fails(new OperationCanceledException(cts.Token))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cycle.RunAsync(cts.Token));

        Assert.False(File.Exists(Path.Combine(_dir, SyncStateStore.FileName)));
    }
}
