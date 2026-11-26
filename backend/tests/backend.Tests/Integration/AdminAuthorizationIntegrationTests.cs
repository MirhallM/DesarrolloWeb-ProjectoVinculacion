using System.Net;
using System.Net.Http.Json;
using backend.Auth;
using backend.Data.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class AdminAuthorizationIntegrationTests : IAsyncLifetime
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
    public async Task Sin_sesion_responde_401_y_usuario_normal_recibe_403_en_rutas_administrativas()
    {
        var factory = CreateFactory();

        var anonSession = new BrowserSession(factory);
        var anonResponse = await anonSession.GetAsync("/api/admin/access");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);
        var anonProblem = await anonResponse.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(anonProblem);
        Assert.Equal(AuthProblemTypes.Unauthenticated, anonProblem.Type);

        var userSession = new BrowserSession(factory);
        var regResponse = await userSession.PostJsonAsync(
            "/api/auth/register",
            new
            {
                email = "cliente.normal@nettes.hn",
                password = "ClaveCliente2026",
                role = "Admin",
                roles = new[] { "Admin" },
                permissions = new[] { AppPermissions.AdminAccess },
            });

        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        var registered = await regResponse.Content.ReadFromJsonAsync<AuthUserResponse>(Ct);
        Assert.NotNull(registered);
        Assert.Equal([AppRoleNames.User], registered.Roles);
        Assert.Equal([AppPermissions.FavoritesManage], registered.Permissions);

        var adminAttempt = await userSession.GetAsync("/api/admin/access");
        Assert.Equal(HttpStatusCode.Forbidden, adminAttempt.StatusCode);
        var forbiddenProblem = await adminAttempt.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(forbiddenProblem);
        Assert.Equal(AuthProblemTypes.Forbidden, forbiddenProblem.Type);
    }

    [PostgresFact]
    public async Task Comando_admin_promote_asigna_rol_admin_a_cuenta_registrada_y_permite_acceder_a_rutas_admin()
    {
        var factory = CreateFactory();

        var session = new BrowserSession(factory);
        var reg = await session.PostJsonAsync(
            "/api/auth/register",
            new RegisterRequest("encargado@nettes.hn", "ClaveEncargado2026"));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

        // Antes de promover: 403
        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/admin/access")).StatusCode);

        // Promoción mediante el comando de consola (idempotente si se repite)
        Assert.Equal(0, await AdminCommand.RunAsync(factory.Services, ["admin", "promote", "  ENCARGADO@nettes.hn "], Ct));
        Assert.Equal(0, await AdminCommand.RunAsync(factory.Services, ["admin", "promote", "encargado@nettes.hn"], Ct));

        // Al iniciar sesión de nuevo recibe rol Admin y todos sus privilegios
        var adminSession = new BrowserSession(factory);
        var login = await adminSession.PostJsonAsync(
            "/api/auth/login",
            new LoginRequest("encargado@nettes.hn", "ClaveEncargado2026"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var loggedIn = await login.Content.ReadFromJsonAsync<AuthUserResponse>(Ct);
        Assert.NotNull(loggedIn);
        Assert.Equal([AppRoleNames.Admin, AppRoleNames.User], loggedIn.Roles);
        Assert.Contains(AppPermissions.AdminAccess, loggedIn.Permissions);
        Assert.Contains(AppPermissions.ProductImagesManage, loggedIn.Permissions);
        Assert.Contains(AppPermissions.FeaturedProductsManage, loggedIn.Permissions);
        Assert.Contains(AppPermissions.SiteContentManage, loggedIn.Permissions);

        var access = await adminSession.GetAsync("/api/admin/access");
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);

        // Si la cuenta administradora se desactiva en PostgreSQL, pierde el acceso al instante con 401
        await using (var db = _database!.CreateContext())
        {
            await db.Users
                .Where(u => u.NormalizedEmail == "ENCARGADO@NETTES.HN")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false), Ct);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await adminSession.GetAsync("/api/admin/access")).StatusCode);
    }

    [PostgresFact]
    public async Task Comando_admin_promote_falla_con_codigo_1_para_correo_inexistente_o_cuenta_inactiva()
    {
        var factory = CreateFactory();

        Assert.Equal(1, await AdminCommand.RunAsync(factory.Services, ["admin", "promote", "noexiste@nettes.hn"], Ct));

        var session = new BrowserSession(factory);
        await session.PostJsonAsync("/api/auth/register", new RegisterRequest("inactivo@nettes.hn", "ClaveInactiva2026"));

        await using (var db = _database!.CreateContext())
        {
            await db.Users
                .Where(u => u.NormalizedEmail == "INACTIVO@NETTES.HN")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false), Ct);
        }

        Assert.Equal(1, await AdminCommand.RunAsync(factory.Services, ["admin", "promote", "inactivo@nettes.hn"], Ct));
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
