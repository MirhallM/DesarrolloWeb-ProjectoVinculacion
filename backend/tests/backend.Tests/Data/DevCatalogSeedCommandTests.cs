using backend.Data.Seed;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace backend.Tests.Data;

/// <summary>
/// <c>seed-dev</c> carga productos falsos (prefijo DEV-). No debe poder correr fuera de Development, sobre todo en Railway
/// (ASPNETCORE_ENVIRONMENT=Production): llenaría el catálogo público con datos de prueba.
/// </summary>
public class DevCatalogSeedCommandTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "backend";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Cuenta cuántas veces se le pide un servicio: si el comando no pasa de la guarda, nunca debe tocarlo.</summary>
    private sealed class CountingServices : IServiceProvider
    {
        public int Calls { get; private set; }

        public object? GetService(Type serviceType)
        {
            Calls++;
            return null;
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("PRODUCTION")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("")]
    public async Task Fuera_de_Development_termina_con_1_sin_tocar_la_base(string environment)
    {
        var services = new CountingServices();

        var exitCode = await DevCatalogSeedCommand.RunAsync(services, new FakeEnvironment(environment), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, services.Calls); // ni siquiera pidió el sembrador: la guarda actúa antes de tocar nada
    }

    [Fact]
    public async Task En_Development_si_pasa_la_guarda()
    {
        // Control: confirma que lo que frena a los demás entornos es la guarda y no otra cosa. Aquí intenta sembrar
        // (pide servicios) y falla porque la prueba no tiene base de datos; lo que importa es que sí lo intentó.
        var services = new CountingServices();

        await DevCatalogSeedCommand.RunAsync(services, new FakeEnvironment("Development"), TestContext.Current.CancellationToken);

        Assert.True(services.Calls > 0);
    }

    [Fact]
    public void Solo_se_activa_con_el_argumento_seed_dev()
    {
        Assert.True(DevCatalogSeedCommand.IsRequested(["seed-dev"]));
        Assert.False(DevCatalogSeedCommand.IsRequested([]));
        Assert.False(DevCatalogSeedCommand.IsRequested(["seed"]));
        Assert.False(DevCatalogSeedCommand.IsRequested(["migrate", "seed-dev"]));
    }
}
