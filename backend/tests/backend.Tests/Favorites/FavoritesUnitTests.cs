using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using backend.Auth;
using backend.Data.Entities;
using backend.Favorites;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace backend.Tests.Favorites;

public sealed class FavoritesUnitTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void MapToDto_proyecta_disponible_y_agotado_cuando_el_producto_es_visible()
    {
        var savedAt = new DateTime(2026, 11, 26, 18, 0, 0, DateTimeKind.Utc);

        var availableDto = FavoritesService.MapToDto(new FavoritesService.RawFavoriteRow(
            Guid.NewGuid(),
            "Leche Entera 1 L",
            34.999m,
            10m,
            IsActive: true,
            "lacteos",
            "Lácteos",
            "/images/leche.webp",
            savedAt));

        Assert.True(availableDto.IsVisible);
        Assert.True(availableDto.Available);
        Assert.Equal(FavoriteAvailabilityStatuses.Available, availableDto.AvailabilityStatus);
        Assert.Equal(35.00m, availableDto.Price);

        var outOfStockDto = FavoritesService.MapToDto(new FavoritesService.RawFavoriteRow(
            Guid.NewGuid(),
            "Arroz Blanco 2 Lb",
            28.50m,
            0m,
            IsActive: true,
            "granos",
            "Granos Básicos",
            null,
            savedAt));

        Assert.True(outOfStockDto.IsVisible);
        Assert.False(outOfStockDto.Available);
        Assert.Equal(FavoriteAvailabilityStatuses.OutOfStock, outOfStockDto.AvailabilityStatus);
        Assert.Equal(28.50m, outOfStockDto.Price);
    }

    [Theory]
    [InlineData(false, 45.00, "Café Molido 400 G", "Café Molido 400 G")]
    [InlineData(true, 0.00, "Aceite Vegetal 1 L", "Aceite Vegetal 1 L")]
    [InlineData(true, 100000.00, "Precio Sospechoso", "Precio Sospechoso")]
    [InlineData(true, 15000000000.00, "Relleno Aspel", "Relleno Aspel")]
    [InlineData(true, 25.00, "   ", "Producto no disponible")]
    public void MapToDto_marca_unavailable_y_oculta_precio_cuando_el_producto_quedo_oculto(
        bool isActive,
        double rawPrice,
        string displayName,
        string expectedName)
    {
        var dto = FavoritesService.MapToDto(new FavoritesService.RawFavoriteRow(
            Guid.NewGuid(),
            displayName,
            (decimal)rawPrice,
            50m,
            isActive,
            "otros",
            "Otros",
            null,
            DateTime.UtcNow));

        Assert.False(dto.IsVisible);
        Assert.False(dto.Available);
        Assert.Equal(FavoriteAvailabilityStatuses.Unavailable, dto.AvailabilityStatus);
        Assert.Null(dto.Price);
        Assert.Equal(expectedName, dto.Name);
    }

    [Fact]
    public void FavoriteItemDto_serializado_nunca_expone_codigo_descripcion_ni_stock()
    {
        var dto = FavoritesService.MapToDto(new FavoritesService.RawFavoriteRow(
            Guid.NewGuid(),
            "Jugo De Naranja 1 L",
            42.00m,
            15m,
            IsActive: true,
            "bebidas",
            "Bebidas",
            null,
            DateTime.UtcNow));

        var json = JsonSerializer.Serialize(dto, WebJson);

        Assert.DoesNotContain("\"code\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"description\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"stock\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rutas_de_favoritos_sin_sesion_responden_401_problem_details_sin_redirigir()
    {
        using var factory = CreateHostFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var productId = Guid.NewGuid();

        var responses = new[]
        {
            await client.GetAsync("/api/favorites", Ct),
            await client.PostAsync($"/api/favorites/{productId}", content: null, Ct),
            await client.DeleteAsync($"/api/favorites/{productId}", Ct),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
            Assert.NotNull(problem);
            Assert.Equal(AuthProblemTypes.Unauthenticated, problem.Type);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Mutaciones_de_favoritos_desde_origen_externo_se_bloquean_con_403_csrf(string method)
    {
        using var factory = CreateHostFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var cookie = CreateUserCookie(factory.Services);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/favorites/{Guid.NewGuid()}");
        request.Headers.Add("Cookie", $"{AuthConstants.DevAuthCookieName}={cookie}");
        request.Headers.Add("Origin", "https://sitio-externo.malicioso");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal(AuthProblemTypes.CsrfRejected, problem.Type);
    }

    [Fact]
    public async Task OpenApi_documenta_las_tres_rutas_de_favoritos()
    {
        using var factory = CreateHostFactory();
        using var client = factory.CreateClient();

        var openApiJson = await client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("\"/api/favorites\"", openApiJson);
        Assert.Contains("\"/api/favorites/{productId}\"", openApiJson);
        Assert.Contains("availabilityStatus", openApiJson);
    }

    private static string CreateUserCookie(IServiceProvider services)
    {
        var cookieOptions = services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "cliente@nettes.hn"),
            new Claim(ClaimTypes.Email, "cliente@nettes.hn"),
            new Claim(ClaimTypes.Role, AppRoleNames.User),
            new Claim("AspNet.Identity.SecurityStamp", Guid.NewGuid().ToString("N")),
        ], IdentityConstants.ApplicationScheme);

        var now = DateTimeOffset.UtcNow;
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddHours(1) },
            IdentityConstants.ApplicationScheme);

        return cookieOptions.TicketDataFormat.Protect(ticket);
    }

    private static WebApplicationFactory<Program> CreateHostFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=pruebas;Username=pruebas;Password=pruebas;Timeout=1");
            builder.ConfigureTestServices(services =>
                services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository()));
        });

    private sealed class InMemoryXmlRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _elements.AsReadOnly();

        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }
}
