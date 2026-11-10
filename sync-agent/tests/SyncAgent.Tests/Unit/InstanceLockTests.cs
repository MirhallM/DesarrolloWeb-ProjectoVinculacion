namespace SyncAgent.Tests.Unit;

public sealed class InstanceLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "syncagent-lock-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Mientras_una_ejecucion_tiene_el_candado_otra_no_puede_tomarlo()
    {
        using var first = InstanceLock.TryAcquire(_dir);
        var second = InstanceLock.TryAcquire(_dir);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Al_soltar_el_candado_otra_ejecucion_puede_tomarlo()
    {
        var first = InstanceLock.TryAcquire(_dir);
        Assert.NotNull(first);
        first.Dispose();

        using var second = InstanceLock.TryAcquire(_dir);

        Assert.NotNull(second);
    }

    [Fact]
    public void Crea_la_carpeta_si_no_existe()
    {
        using var held = InstanceLock.TryAcquire(Path.Combine(_dir, "data"));

        Assert.NotNull(held);
        Assert.True(File.Exists(Path.Combine(_dir, "data", InstanceLock.FileName)));
    }

    [Fact]
    public void Carpetas_distintas_tienen_candados_independientes()
    {
        using var a = InstanceLock.TryAcquire(Path.Combine(_dir, "a"));
        using var b = InstanceLock.TryAcquire(Path.Combine(_dir, "b"));

        Assert.NotNull(a);
        Assert.NotNull(b);
    }
}

public class AgentOptionsTests
{
    [Fact]
    public void Una_ruta_relativa_cuelga_de_la_carpeta_del_programa()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "SyncAgentApp");

        var resolved = new AgentOptions { DataDirectory = "data" }.ResolveDataDirectory(baseDir);

        Assert.Equal(Path.Combine(baseDir, "data"), resolved);
    }

    [Fact]
    public void Una_ruta_absoluta_se_respeta()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "datos-del-agente"));

        var resolved = new AgentOptions { DataDirectory = absolute }.ResolveDataDirectory(Path.Combine(Path.GetTempPath(), "otra"));

        Assert.Equal(absolute, resolved);
    }

    [Fact]
    public void Los_valores_por_defecto_son_validos_y_conservan_14_dias()
    {
        var options = new AgentOptions();

        Assert.Empty(options.Validate());
        Assert.Equal(14, options.LogRetentionDays);
    }

    [Theory]
    [InlineData("", 14)]
    [InlineData("data", 0)]
    [InlineData("data", -3)]
    public void Detecta_valores_invalidos(string directory, int days)
    {
        Assert.NotEmpty(new AgentOptions { DataDirectory = directory, LogRetentionDays = days }.Validate());
    }
}
