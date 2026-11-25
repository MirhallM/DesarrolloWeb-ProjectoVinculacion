using System.Net;
using System.Net.Http.Json;
using System.Text;
using backend.Auth;
using backend.Data;
using backend.Data.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class AuthApiIntegrationTests : IAsyncLifetime
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
    public async Task Registro_valido_crea_usuario_con_rol_user_hashea_contrasena_e_inicia_sesion()
    {
        var factory = CreateFactory();
        var session = new BrowserSession(factory);
        const string plainPassword = "MiClaveSegura2026";

        var registerResponse = await session.PostJsonAsync(
            "/api/auth/register",
            new { email = "  Cliente.Nuevo@Nettes.HN ", password = plainPassword, role = "Admin", isAdmin = true });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var created = await registerResponse.Content.ReadFromJsonAsync<AuthUserResponse>(Ct);
        Assert.NotNull(created);
        Assert.Equal("cliente.nuevo@nettes.hn", created.Email);
        Assert.Equal([AppRoleNames.User], created.Roles);

        var meResponse = await session.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<AuthUserResponse>(Ct);
        Assert.NotNull(me);
        Assert.Equal(created.Id, me.Id);
        Assert.Equal([AppRoleNames.User], me.Roles);

        await using var db = _database!.CreateContext();
        var storedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == created.Id, Ct);
        Assert.Equal("cliente.nuevo@nettes.hn", storedUser.Email);
        Assert.Equal("cliente.nuevo@nettes.hn", storedUser.UserName);
        Assert.True(storedUser.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(storedUser.PasswordHash));
        Assert.NotEqual(plainPassword, storedUser.PasswordHash);
        Assert.DoesNotContain(plainPassword, storedUser.PasswordHash, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash!, plainPassword));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var rolesInDb = await userManager.GetRolesAsync(storedUser);
        Assert.Equal([AppRoleNames.User], rolesInDb);
    }

    [PostgresFact]
    public async Task Registro_con_correo_duplicado_responde_409_sin_duplicar_filas()
    {
        var factory = CreateFactory();
        var first = new BrowserSession(factory);

        var r1 = await first.PostJsonAsync("/api/auth/register", new RegisterRequest("repetido@nettes.hn", "ClaveValida123"));
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        var second = new BrowserSession(factory);
        var r2 = await second.PostJsonAsync("/api/auth/register", new RegisterRequest("  REPETIDO@NETTES.HN ", "OtraClaveValida456"));

        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        var problem = await r2.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal(AuthProblemTypes.DuplicateEmail, problem.Type);

        await using var db = _database!.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedEmail == "REPETIDO@NETTES.HN", Ct));
    }

    [PostgresFact]
    public async Task Registro_con_datos_invalidos_responde_400_validation_problem_y_no_crea_usuario()
    {
        var factory = CreateFactory();
        var session = new BrowserSession(factory);

        var response = await session.PostJsonAsync("/api/auth/register", new RegisterRequest("correo-invalido", "123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Contains("email", problem.Errors.Keys);
        Assert.Contains("password", problem.Errors.Keys);

        await using var db = _database!.CreateContext();
        Assert.Empty(await db.Users.ToListAsync(Ct));
    }

    [PostgresFact]
    public async Task Flujo_completo_registro_logout_login_valido_y_logout()
    {
        var factory = CreateFactory();
        var session = new BrowserSession(factory);

        var reg = await session.PostJsonAsync("/api/auth/register", new RegisterRequest("flujo@nettes.hn", "ClaveSegura789"));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

        var logout1 = await session.PostEmptyAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout1.StatusCode);

        var meAfterLogout = await session.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);

        var loginSession = new BrowserSession(factory);
        var login = await loginSession.PostJsonAsync(
            "/api/auth/login",
            new LoginRequest("  FLUJO@nettes.hn ", "ClaveSegura789", RememberMe: true));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loggedIn = await login.Content.ReadFromJsonAsync<AuthUserResponse>(Ct);
        Assert.NotNull(loggedIn);
        Assert.Equal("flujo@nettes.hn", loggedIn.Email);
        Assert.Equal([AppRoleNames.User], loggedIn.Roles);

        var meAfterLogin = await loginSession.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meAfterLogin.StatusCode);
    }

    [PostgresFact]
    public async Task Login_invalido_devuelve_el_mismo_401_para_correo_inexistente_clave_erronea_y_cuenta_inactiva()
    {
        var factory = CreateFactory(
            ("Auth:SecurityStampValidationMinutes", "0"),
            ("Auth:MaxFailedAccessAttempts", "10"));

        var activeSession = new BrowserSession(factory);
        await activeSession.PostJsonAsync("/api/auth/register", new RegisterRequest("activo@nettes.hn", "ClaveCorrecta123"));

        var inactiveSession = new BrowserSession(factory);
        var regInactive = await inactiveSession.PostJsonAsync(
            "/api/auth/register",
            new RegisterRequest("inactivo@nettes.hn", "ClaveCorrecta123"));
        var inactiveUser = (await regInactive.Content.ReadFromJsonAsync<AuthUserResponse>(Ct))!;

        await using (var db = _database!.CreateContext())
        {
            await db.Users.Where(u => u.Id == inactiveUser.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false), Ct);
        }

        var meDeactivated = await inactiveSession.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meDeactivated.StatusCode);

        var probe = new BrowserSession(factory);

        var wrongPasswordResp = await probe.PostJsonAsync("/api/auth/login", new LoginRequest("activo@nettes.hn", "ClaveEquivocada999"));
        var unknownEmailResp = await probe.PostJsonAsync("/api/auth/login", new LoginRequest("noexiste@nettes.hn", "ClaveCorrecta123"));
        var inactiveLoginResp = await probe.PostJsonAsync("/api/auth/login", new LoginRequest("inactivo@nettes.hn", "ClaveCorrecta123"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResp.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmailResp.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveLoginResp.StatusCode);

        var p1 = (await wrongPasswordResp.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        var p2 = (await unknownEmailResp.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        var p3 = (await inactiveLoginResp.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;

        Assert.Equal(AuthProblemTypes.InvalidCredentials, p1.Type);
        Assert.Equal(p1.Type, p2.Type);
        Assert.Equal(p1.Type, p3.Type);
        Assert.Equal(p1.Detail, p2.Detail);
        Assert.Equal(p1.Detail, p3.Detail);
    }

    [PostgresFact]
    public async Task Intentos_fallidos_repetidos_bloquean_temporalmente_la_cuenta_incluso_con_la_clave_correcta()
    {
        var factory = CreateFactory(("Auth:MaxFailedAccessAttempts", "3"), ("Auth:LockoutMinutes", "15"));

        var setup = new BrowserSession(factory);
        await setup.PostJsonAsync("/api/auth/register", new RegisterRequest("bloqueo@nettes.hn", "ClaveValida123"));

        var attacker = new BrowserSession(factory);

        var attempt1 = await attacker.PostJsonAsync("/api/auth/login", new LoginRequest("bloqueo@nettes.hn", "Incorrecta1"));
        var attempt2 = await attacker.PostJsonAsync("/api/auth/login", new LoginRequest("bloqueo@nettes.hn", "Incorrecta2"));
        var attempt3 = await attacker.PostJsonAsync("/api/auth/login", new LoginRequest("bloqueo@nettes.hn", "Incorrecta3"));

        Assert.Equal(HttpStatusCode.Unauthorized, attempt1.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, attempt2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, attempt3.StatusCode);

        var validWhileLocked = await attacker.PostJsonAsync("/api/auth/login", new LoginRequest("bloqueo@nettes.hn", "ClaveValida123"));
        Assert.Equal(HttpStatusCode.TooManyRequests, validWhileLocked.StatusCode);
        var lockedProblem = await validWhileLocked.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(lockedProblem);
        Assert.Equal(AuthProblemTypes.AccountLocked, lockedProblem.Type);
    }

    [PostgresFact]
    public async Task En_produccion_detras_de_https_emite_cookie_host_secure_y_sobrevive_a_un_contenedor_nuevo()
    {
        string? rawAuthCookieHeader;
        Guid userId;

        await using (var firstHost = CreateFactory(environment: "Production"))
        {
            var session = new BrowserSession(firstHost, forwardedHttps: true);

            var register = await session.PostJsonAsync(
                "/api/auth/register",
                new RegisterRequest("prod@nettes.hn", "ClaveProduccion2026"));

            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            userId = (await register.Content.ReadFromJsonAsync<AuthUserResponse>(Ct))!.Id;

            var setCookies = register.Headers.GetValues("Set-Cookie").ToList();
            rawAuthCookieHeader = Assert.Single(
                setCookies,
                c => c.StartsWith($"{AuthConstants.ProdAuthCookieName}=", StringComparison.Ordinal));

            Assert.Contains("secure", rawAuthCookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", rawAuthCookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", rawAuthCookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", rawAuthCookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", rawAuthCookieHeader, StringComparison.OrdinalIgnoreCase);
        }

        await using var secondHost = CreateFactory(environment: "Production");
        using var secondClient = secondHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var cookiePair = rawAuthCookieHeader.Split(';', 2)[0];

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("Cookie", cookiePair);

        var meResponse = await secondClient.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var me = await meResponse.Content.ReadFromJsonAsync<AuthUserResponse>(Ct);
        Assert.NotNull(me);
        Assert.Equal(userId, me.Id);
        Assert.Equal("prod@nettes.hn", me.Email);
    }

    private WebApplicationFactory<Program> CreateFactory(
        params (string Key, string Value)[] settings) =>
        CreateFactory("Development", settings);

    private WebApplicationFactory<Program> CreateFactory(
        string environment,
        params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:DefaultConnection", _database!.ConnectionString);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            // TestServer deja RemoteIpAddress en null; se asigna una IP privada para ejercitar UseForwardedHeaders.
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new SimulatedPrivateProxyStartupFilter()));
        });
        _factories.Add(factory);
        return factory;
    }

    private sealed class SimulatedPrivateProxyStartupFilter : IStartupFilter
    {
        private static readonly IPAddress RailwayInternalIp = IPAddress.Parse("fd12:3456::20");

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                ctx.Connection.RemoteIpAddress ??= RailwayInternalIp;
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }

    private sealed class BrowserSession(WebApplicationFactory<Program> factory, bool forwardedHttps = false)
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

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, path);
            if (forwardedHttps)
            {
                request.Headers.Add("X-Forwarded-Proto", "https");
                request.Headers.Add("X-Forwarded-Host", "nettes.up.railway.app");
                request.Headers.Add("Origin", "https://nettes.up.railway.app");
                request.Headers.Add("Sec-Fetch-Site", "same-origin");
            }
            else
            {
                request.Headers.Add("Origin", "http://localhost");
                request.Headers.Add("Sec-Fetch-Site", "same-origin");
            }

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
