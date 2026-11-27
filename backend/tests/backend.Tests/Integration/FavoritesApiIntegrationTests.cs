using System.Net;
using System.Net.Http.Json;
using System.Text;
using backend.Auth;
using backend.Data.Entities;
using backend.Favorites;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class FavoritesApiIntegrationTests : IAsyncLifetime
{
    private TempDatabase? _database;
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (IntegrationEnvironment.IsConfigured)
        {
            _database = await TempDatabase.CreateAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task Favoritos_se_conservan_entre_sesiones_estan_aislados_por_usuario_y_no_exponen_codigo()
    {
        var factory = CreateFactory();
        var (visibleA, visibleB, _) = await SeedSampleProductsAsync();

        // Usuario 1 registra cuenta y guarda dos favoritos.
        var user1Session1 = new BrowserSession(factory);
        await user1Session1.PostJsonAsync("/api/auth/register", new RegisterRequest("ana@nettes.hn", "ClaveAna2026"));

        var add1 = await user1Session1.PostEmptyAsync($"/api/favorites/{visibleA}");
        Assert.Equal(HttpStatusCode.Created, add1.StatusCode);

        var add2 = await user1Session1.PostEmptyAsync($"/api/favorites/{visibleB}");
        Assert.Equal(HttpStatusCode.Created, add2.StatusCode);

        // Cierra sesión y vuelve a iniciar en un navegador nuevo -> los favoritos persisten.
        Assert.Equal(HttpStatusCode.NoContent, (await user1Session1.PostEmptyAsync("/api/auth/logout")).StatusCode);

        var user1Session2 = new BrowserSession(factory);
        await user1Session2.PostJsonAsync("/api/auth/login", new LoginRequest("ana@nettes.hn", "ClaveAna2026"));

        var listResp = await user1Session2.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        var rawJson = await listResp.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("FAV-CODE", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"code\"", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"stock\"", rawJson, StringComparison.OrdinalIgnoreCase);

        var user1Favorites = await listResp.Content.ReadFromJsonAsync<List<FavoriteItemDto>>(Ct);
        Assert.NotNull(user1Favorites);
        Assert.Equal(2, user1Favorites.Count);
        Assert.Contains(user1Favorites, f => f.ProductId == visibleA && f.AvailabilityStatus == FavoriteAvailabilityStatuses.Available);
        Assert.Contains(user1Favorites, f => f.ProductId == visibleB && f.AvailabilityStatus == FavoriteAvailabilityStatuses.OutOfStock);

        // Usuario 2 no ve los favoritos de Usuario 1 ni puede borrarlos.
        var user2Session = new BrowserSession(factory);
        await user2Session.PostJsonAsync("/api/auth/register", new RegisterRequest("luis@nettes.hn", "ClaveLuis2026"));

        var user2Initial = await (await user2Session.GetAsync("/api/favorites")).Content.ReadFromJsonAsync<List<FavoriteItemDto>>(Ct);
        Assert.NotNull(user2Initial);
        Assert.Empty(user2Initial);

        var deleteByUser2 = await user2Session.DeleteAsync($"/api/favorites/{visibleA}");
        Assert.Equal(HttpStatusCode.NoContent, deleteByUser2.StatusCode);

        var user1AfterUser2Delete = await (await user1Session2.GetAsync("/api/favorites")).Content.ReadFromJsonAsync<List<FavoriteItemDto>>(Ct);
        Assert.Equal(2, user1AfterUser2Delete!.Count);
    }

    [PostgresFact]
    public async Task Agregar_valida_producto_visible_e_impide_duplicados_del_mismo_usuario()
    {
        var factory = CreateFactory();
        var (visibleId, _, hiddenId) = await SeedSampleProductsAsync();

        var session = new BrowserSession(factory);
        await session.PostJsonAsync("/api/auth/register", new RegisterRequest("duplicados@nettes.hn", "ClaveSegura2026"));

        // Producto inexistente -> 404
        var missingResp = await session.PostEmptyAsync($"/api/favorites/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missingResp.StatusCode);
        Assert.Equal(
            FavoriteProblemTypes.ProductNotFound,
            (await missingResp.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Type);

        // Producto oculto (inactivo / sin precio válido) -> 404 y no se guarda
        var hiddenResp = await session.PostEmptyAsync($"/api/favorites/{hiddenId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenResp.StatusCode);

        // Primer guardado de producto visible -> 201 Created
        var firstAdd = await session.PostEmptyAsync($"/api/favorites/{visibleId}");
        Assert.Equal(HttpStatusCode.Created, firstAdd.StatusCode);

        // Segundo guardado del mismo producto por el mismo usuario -> 409 Conflict sin duplicar fila
        var duplicateAdd = await session.PostEmptyAsync($"/api/favorites/{visibleId}");
        Assert.Equal(HttpStatusCode.Conflict, duplicateAdd.StatusCode);
        Assert.Equal(
            FavoriteProblemTypes.AlreadyExists,
            (await duplicateAdd.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Type);

        await using var db = _database!.CreateContext();
        Assert.Equal(1, await db.Favorites.CountAsync(Ct));
    }

    [PostgresFact]
    public async Task Producto_favorito_que_despues_queda_oculto_se_muestra_como_unavailable_y_puede_eliminarse()
    {
        var factory = CreateFactory();
        var (product1, product2, _) = await SeedSampleProductsAsync();

        var session = new BrowserSession(factory);
        await session.PostJsonAsync("/api/auth/register", new RegisterRequest("ocultos@nettes.hn", "ClaveOcultos2026"));

        Assert.Equal(HttpStatusCode.Created, (await session.PostEmptyAsync($"/api/favorites/{product1}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await session.PostEmptyAsync($"/api/favorites/{product2}")).StatusCode);

        // Simular que tras una sincronización product1 queda inactivo (STATUS = 'B') y product2 recibe un precio de relleno >= 100000.
        await using (var db = _database!.CreateContext())
        {
            await db.Products.Where(p => p.Id == product1).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false), Ct);
            await db.Products.Where(p => p.Id == product2).ExecuteUpdateAsync(s => s.SetProperty(p => p.PublicPrice, 15_000_000_000m), Ct);
        }

        var listResp = await session.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var items = (await listResp.Content.ReadFromJsonAsync<List<FavoriteItemDto>>(Ct))!;
        Assert.Equal(2, items.Count);

        Assert.All(items, item =>
        {
            Assert.False(item.IsVisible);
            Assert.False(item.Available);
            Assert.Equal(FavoriteAvailabilityStatuses.Unavailable, item.AvailabilityStatus);
            Assert.Null(item.Price);
        });

        // El usuario puede quitar de su lista un producto aunque haya quedado oculto.
        var del = await session.DeleteAsync($"/api/favorites/{product1}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var remaining = (await (await session.GetAsync("/api/favorites")).Content.ReadFromJsonAsync<List<FavoriteItemDto>>(Ct))!;
        var single = Assert.Single(remaining);
        Assert.Equal(product2, single.ProductId);
    }

    private async Task<(Guid VisibleAvailableId, Guid VisibleOutOfStockId, Guid HiddenId)> SeedSampleProductsAsync()
    {
        var visibleAvailableId = Guid.NewGuid();
        var visibleOutOfStockId = Guid.NewGuid();
        var hiddenId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = _database!.CreateContext();
        db.Products.AddRange(
            new Product
            {
                Id = visibleAvailableId,
                Code = "FAV-CODE-001",
                Description = "LECHE ENT 1LT",
                DisplayName = "Leche Entera 1 L",
                CategoryId = Category.OthersId,
                PublicPrice = 36.50m,
                Stock = 8m,
                IsActive = true,
                SyncedAt = now,
            },
            new Product
            {
                Id = visibleOutOfStockId,
                Code = "FAV-CODE-002",
                Description = "CAFE MOLIDO 400G",
                DisplayName = "Café Molido 400 G",
                CategoryId = Category.OthersId,
                PublicPrice = 95.00m,
                Stock = 0m,
                IsActive = true,
                SyncedAt = now,
            },
            new Product
            {
                Id = hiddenId,
                Code = "FAV-CODE-003",
                Description = "BAJA EN ASPEL",
                DisplayName = "Producto Dado De Baja",
                CategoryId = Category.OthersId,
                PublicPrice = 40.00m,
                Stock = 5m,
                IsActive = false,
                SyncedAt = now,
            });

        await db.SaveChangesAsync(Ct);
        return (visibleAvailableId, visibleOutOfStockId, hiddenId);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _database!.ConnectionString);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new SimulatedPrivateProxyStartupFilter()));
        });
        _factories.Add(factory);
        return factory;
    }

    private sealed class SimulatedPrivateProxyStartupFilter : IStartupFilter
    {
        private static readonly IPAddress LoopbackIp = IPAddress.Loopback;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                ctx.Connection.RemoteIpAddress ??= LoopbackIp;
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }

    private sealed class BrowserSession(WebApplicationFactory<Program> factory)
    {
        private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        public async Task<HttpResponseMessage> GetAsync(string path)
        {
            using var request = CreateRequest(HttpMethod.Get, path);
            return await SendAndCaptureAsync(request);
        }

        public async Task<HttpResponseMessage> PostJsonAsync<T>(string path, T body)
        {
            using var request = CreateRequest(HttpMethod.Post, path);
            request.Content = JsonContent.Create(body);
            return await SendAndCaptureAsync(request);
        }

        public async Task<HttpResponseMessage> PostEmptyAsync(string path)
        {
            using var request = CreateRequest(HttpMethod.Post, path);
            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            return await SendAndCaptureAsync(request);
        }

        public async Task<HttpResponseMessage> DeleteAsync(string path)
        {
            using var request = CreateRequest(HttpMethod.Delete, path);
            return await SendAndCaptureAsync(request);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Origin", "http://localhost");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");

            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            return request;
        }

        private async Task<HttpResponseMessage> SendAndCaptureAsync(HttpRequestMessage request)
        {
            var response = await _client.SendAsync(request, Ct);

            if (response.Headers.TryGetValues("Set-Cookie", out var rawCookies))
            {
                foreach (var raw in rawCookies)
                {
                    var nameValue = raw.Split(';', 2)[0];
                    var eq = nameValue.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    var name = nameValue[..eq];
                    var value = nameValue[(eq + 1)..];
                    if (string.IsNullOrEmpty(value))
                    {
                        _cookies.Remove(name);
                    }
                    else
                    {
                        _cookies[name] = value;
                    }
                }
            }

            return response;
        }
    }
}
