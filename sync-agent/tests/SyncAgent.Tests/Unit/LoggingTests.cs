using Microsoft.Extensions.Logging;
using SyncAgent.Diagnostics;
using SyncAgent.Logging;

namespace SyncAgent.Tests.Unit;

public sealed class LoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "syncagent-tests-" + Guid.NewGuid().ToString("N"));

    public LoggingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    // ---------- Retención de 14 días ----------

    [Fact]
    public void Borra_solo_los_logs_con_mas_de_14_dias()
    {
        var today = new DateTime(2026, 11, 20);
        var hace13 = Touch(FileLoggerProvider.FileNameFor(today.AddDays(-13)));
        var hace14 = Touch(FileLoggerProvider.FileNameFor(today.AddDays(-14)));
        var hace15 = Touch(FileLoggerProvider.FileNameFor(today.AddDays(-15)));
        var hace60 = Touch(FileLoggerProvider.FileNameFor(today.AddDays(-60)));

        var deleted = LogRetention.Cleanup(_dir, 14, today);

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(hace13));
        Assert.True(File.Exists(hace14));
        Assert.False(File.Exists(hace15));
        Assert.False(File.Exists(hace60));
    }

    [Fact]
    public void No_toca_archivos_que_no_son_logs_del_agente()
    {
        var today = new DateTime(2026, 11, 20);
        var otro = Touch("notas.txt");
        var raro = Touch("agent-no-es-fecha.log");
        var sync = Touch("sync-state.json");

        LogRetention.Cleanup(_dir, 14, today);

        Assert.True(File.Exists(otro));
        Assert.True(File.Exists(raro));
        Assert.True(File.Exists(sync));
    }

    [Fact]
    public void Carpeta_inexistente_no_falla()
    {
        Assert.Equal(0, LogRetention.Cleanup(Path.Combine(_dir, "no-existe"), 14, DateTime.Today));
    }

    [Fact]
    public void El_periodo_de_retencion_es_configurable()
    {
        var today = new DateTime(2026, 11, 20);
        var hace4 = Touch(FileLoggerProvider.FileNameFor(today.AddDays(-4)));

        Assert.Equal(1, LogRetention.Cleanup(_dir, 3, today));
        Assert.False(File.Exists(hace4));
    }

    // ---------- Archivo de log con redacción ----------

    [Fact]
    public void El_log_en_archivo_oculta_secretos_incluso_dentro_de_excepciones()
    {
        var redactor = new SecretRedactor(["Cl4ve-Secreta", "usuario_x"]);
        var now = new DateTime(2026, 11, 20, 10, 30, 0);
        using var provider = new FileLoggerProvider(_dir, redactor, () => now);
        var logger = provider.CreateLogger("SyncAgent.Prueba");

        logger.LogInformation("Conectando con usuario_x y clave Cl4ve-Secreta");
        logger.LogError(new InvalidOperationException("Login failed for user 'usuario_x' Cl4ve-Secreta"), "Falló");

        var text = File.ReadAllText(Path.Combine(_dir, "agent-2026-11-20.log"));
        Assert.DoesNotContain("Cl4ve-Secreta", text);
        Assert.DoesNotContain("usuario_x", text);
        Assert.Contains("[INF] Prueba: Conectando", text);
        Assert.Contains("[ERR]", text);
    }

    [Fact]
    public void Si_no_se_puede_escribir_el_log_no_lanza_excepcion()
    {
        // La "carpeta" es en realidad un archivo: crear/escribir falla, pero el agente debe seguir.
        var file = Touch("ocupado");
        using var provider = new FileLoggerProvider(Path.Combine(file, "logs"), SecretRedactor.None);

        var ex = Record.Exception(() => provider.CreateLogger("X").LogInformation("hola"));

        Assert.Null(ex);
    }
}
