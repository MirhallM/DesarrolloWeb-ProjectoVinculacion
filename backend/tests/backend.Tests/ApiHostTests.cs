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
}
