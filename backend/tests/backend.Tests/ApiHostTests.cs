using System.Net;
using backend.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

public class ApiHostTests
{
    [Fact]
    public void La_api_construye_su_host_y_resuelve_el_contexto_de_datos()
    {
        // La cadena es falsa: construir el host y resolver AppDbContext no abre ninguna conexión a PostgreSQL.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=pruebas;Username=pruebas;Password=pruebas"));

        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task En_desarrollo_expone_openapi_y_swagger_ui()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=pruebas;Username=pruebas;Password=pruebas"));

        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var openApiResponse = await client.GetAsync("/openapi/v1.json", ct);
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        var openApiJson = await openApiResponse.Content.ReadAsStringAsync(ct);
        Assert.Contains("/api/products", openApiJson);
        Assert.Contains("/api/categories", openApiJson);

        var swaggerResponse = await client.GetAsync("/swagger/index.html", ct);
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);
    }

    [Fact]
    public async Task Health_responde_503_si_no_hay_postgresql()
    {
        // Puerto 1: nada escucha ahí, así que el chequeo de la base falla rápido. Demuestra que /api/health
        // existe y que de verdad consulta PostgreSQL (no responde 200 a ciegas).
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=pruebas;Username=pruebas;Password=pruebas;Timeout=2"));

        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/api/health", ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData(new[] { "migrate" }, true)]
    [InlineData(new[] { "migrate", "extra" }, true)]
    [InlineData(new[] { "seed" }, false)]
    [InlineData(new[] { "Migrate" }, false)]
    [InlineData(new string[0], false)]
    public void MigrateCommand_se_reconoce_solo_como_primer_argumento_exacto(string[] args, bool expected)
    {
        Assert.Equal(expected, MigrateCommand.IsRequested(args));
    }
}
