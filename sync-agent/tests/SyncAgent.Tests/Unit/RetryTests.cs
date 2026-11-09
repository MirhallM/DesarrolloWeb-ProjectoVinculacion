using Microsoft.Extensions.Logging.Abstractions;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Resilience;

namespace SyncAgent.Tests.Unit;

public class RetryPolicyTests
{
    private static (RetryPolicy Policy, List<TimeSpan> Waits) Create(int maxAttempts, params int[] delays)
    {
        var waits = new List<TimeSpan>();
        var policy = new RetryPolicy(
            new AspelOptions.RetryOptions { MaxAttempts = maxAttempts, DelaysSeconds = delays },
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        return (policy, waits);
    }

    [Fact]
    public async Task Reintenta_los_fallos_transitorios_con_las_esperas_configuradas()
    {
        var (policy, waits) = Create(4, 5, 15, 45);

        var result = await policy.ExecuteAsync(
            (attempt, _) => attempt < 4 ? throw new TimeoutException() : Task.FromResult(attempt),
            ex => ex is TimeoutException, null, TestContext.Current.CancellationToken);

        Assert.Equal(4, result);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)], waits);
    }

    [Fact]
    public async Task No_reintenta_los_errores_que_no_son_transitorios()
    {
        var (policy, waits) = Create(4, 5);
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ExecuteAsync<int>(
            (_, _) => { calls++; throw new InvalidOperationException(); },
            ex => ex is TimeoutException, null, TestContext.Current.CancellationToken));

        Assert.Equal(1, calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Respeta_el_maximo_de_intentos_y_propaga_el_ultimo_error()
    {
        var (policy, _) = Create(3, 1);
        var calls = 0;

        await Assert.ThrowsAsync<TimeoutException>(() => policy.ExecuteAsync<int>(
            (_, _) => { calls++; throw new TimeoutException(); },
            ex => ex is TimeoutException, null, TestContext.Current.CancellationToken));

        Assert.Equal(3, calls);
    }

    [Fact]
    public void Si_faltan_esperas_se_repite_la_ultima()
    {
        var (policy, _) = Create(5, 5, 15);

        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(15), policy.DelayBefore(2));
        Assert.Equal(TimeSpan.FromSeconds(15), policy.DelayBefore(4));
    }

    [Fact]
    public async Task Sin_esperas_configuradas_reintenta_de_inmediato()
    {
        var (policy, waits) = Create(2);

        await policy.ExecuteAsync(
            (attempt, _) => attempt < 2 ? throw new TimeoutException() : Task.FromResult(1),
            ex => ex is TimeoutException, null, TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.Zero], waits);
    }
}

public class SqlTransientErrorsTests
{
    [Theory]
    [InlineData(-2)]
    [InlineData(53)]
    [InlineData(40)]
    [InlineData(121)]
    [InlineData(233)]
    [InlineData(1205)]
    [InlineData(4060)]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    [InlineData(10061)]
    [InlineData(11001)]
    public void Estos_numeros_se_reintentan(int number) => Assert.True(SqlTransientErrors.IsTransientNumber(number));

    [Theory]
    [InlineData(18456)] // login fallido: reintentar solo bloquearía la cuenta
    [InlineData(229)]   // permiso denegado
    [InlineData(262)]
    [InlineData(207)]   // columna inexistente
    [InlineData(208)]   // tabla inexistente
    public void Estos_numeros_no_se_reintentan(int number) => Assert.False(SqlTransientErrors.IsTransientNumber(number));

    [Fact]
    public void TimeoutException_es_transitoria_y_otras_excepciones_no()
    {
        Assert.True(SqlTransientErrors.IsTransient(new TimeoutException()));
        Assert.False(SqlTransientErrors.IsTransient(new InvalidOperationException()));
    }
}

public class AspelProductLoaderTests
{
    private static AspelProduct P(string code) => new(code, "D", 1, "A", 1);

    private sealed class FakeReader(params Func<IAsyncEnumerable<AspelProduct>>[] attempts) : IAspelProductReader
    {
        public int Calls { get; private set; }

        public IAsyncEnumerable<AspelProduct> ReadAsync(AspelReadMetrics? metrics = null, CancellationToken cancellationToken = default)
            => attempts[Math.Min(Calls++, attempts.Length - 1)]();
    }

    private static async IAsyncEnumerable<AspelProduct> Rows(params AspelProduct[] products)
    {
        foreach (var p in products)
        {
            await Task.Yield();
            yield return p;
        }
    }

    private static async IAsyncEnumerable<AspelProduct> FailsAfterOneRow(Exception ex)
    {
        await Task.Yield();
        yield return P("PARCIAL");
        throw ex;
    }

    private static AspelProductLoader Loader(IAspelProductReader reader, int maxAttempts = 3) =>
        new(reader,
            new RetryPolicy(new AspelOptions.RetryOptions { MaxAttempts = maxAttempts, DelaysSeconds = [0] }, (_, _) => Task.CompletedTask),
            new SqlErrorDescriber(SecretRedactor.None),
            NullLogger<AspelProductLoader>.Instance);

    [Fact]
    public async Task Lectura_correcta_devuelve_todos_los_productos_en_un_intento()
    {
        var reader = new FakeReader(() => Rows(P("A"), P("B")));

        var result = await Loader(reader).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Products.Count);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Si_la_conexion_falla_a_mitad_se_descarta_lo_parcial_y_se_relee_completo()
    {
        var reader = new FakeReader(
            () => FailsAfterOneRow(new TimeoutException()),
            () => Rows(P("A"), P("B"), P("C")));

        var result = await Loader(reader).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Attempts);
        Assert.Equal(["A", "B", "C"], result.Products.Select(p => p.Code));
        Assert.DoesNotContain(result.Products, p => p.Code == "PARCIAL");
    }

    [Fact]
    public async Task Agotados_los_intentos_lanza_AspelReadException_sin_la_excepcion_original()
    {
        var reader = new FakeReader(() => FailsAfterOneRow(new TimeoutException("Login failed for user 'catalogo_lectura'")));

        var ex = await Assert.ThrowsAsync<AspelReadException>(() => Loader(reader, 3).LoadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(3, reader.Calls);
        Assert.Null(ex.InnerException); // nada que pueda filtrar el usuario al registrar ex.ToString()
        Assert.DoesNotContain("catalogo_lectura", ex.ToString());
    }

    [Fact]
    public async Task Un_error_no_transitorio_no_se_reintenta()
    {
        var reader = new FakeReader(() => FailsAfterOneRow(new InvalidOperationException("esquema distinto")));

        await Assert.ThrowsAsync<AspelReadException>(() => Loader(reader).LoadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task La_cancelacion_se_propaga_sin_envolverse()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var reader = new FakeReader(() => FailsAfterOneRow(new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Loader(reader).LoadAsync(cts.Token));
    }
}
