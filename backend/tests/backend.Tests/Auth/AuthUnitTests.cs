using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using backend.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests.Auth;

public sealed class AuthUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("cliente@nettes.hn", "cliente@nettes.hn")]
    [InlineData("  Cliente.Prueba@MiniSuperNettes.COM  ", "cliente.prueba@minisupernettes.com")]
    [InlineData("usuario+favoritos@dominio.org", "usuario+favoritos@dominio.org")]
    public void TryNormalizeEmail_acepta_correos_validos_y_normaliza_a_minusculas(string input, string expected)
    {
        var ok = AuthValidation.TryNormalizeEmail(input, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba.com")]
    [InlineData("doble@@dominio.com")]
    [InlineData("@dominio.com")]
    [InlineData("usuario@")]
    [InlineData("usuario@dominio")]
    [InlineData("usuario@.dominio.com")]
    [InlineData("usuario@dominio.com.")]
    [InlineData("usuario@dom..inio.com")]
    [InlineData("usuario con espacio@dominio.com")]
    public void TryNormalizeEmail_rechaza_formatos_invalidos(string? input)
    {
        var ok = AuthValidation.TryNormalizeEmail(input, out var normalized, out var error);

        Assert.False(ok);
        Assert.Empty(normalized);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryNormalizeEmail_rechaza_correos_que_superan_256_caracteres()
    {
        var longEmail = new string('a', 250) + "@nettes.hn";

        var ok = AuthValidation.TryNormalizeEmail(longEmail, out _, out var error);

        Assert.False(ok);
        Assert.Contains("256", error);
    }

    [Theory]
    [InlineData("Clave1234")]
    [InlineData("CLAVE1234")]
    [InlineData("mi clave segura 99")]
    public void ValidateRegister_acepta_contrasenas_validas_y_respeta_espacios(string password)
    {
        var errors = AuthValidation.ValidateRegister(new RegisterRequest("cliente@nettes.hn", password), out var email);

        Assert.Empty(errors);
        Assert.Equal("cliente@nettes.hn", email);
    }

    [Theory]
    [InlineData("", "obligatoria")]
    [InlineData("Abc123", "al menos 8")]
    [InlineData("1234567890", "letra")]
    [InlineData("soloLetrasLarguisima", "número")]
    [InlineData("aaaa1111", "distintos")]
    public void ValidateRegister_rechaza_contrasenas_debiles(string password, string expectedFragment)
    {
        var errors = AuthValidation.ValidateRegister(new RegisterRequest("cliente@nettes.hn", password), out _);

        Assert.True(errors.TryGetValue("password", out var passwordErrors));
        Assert.Contains(passwordErrors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRegister_y_ValidateLogin_limitan_largo_maximo_de_contrasena_contra_abuso_pbkdf2()
    {
        var hugePassword = "A1" + new string('x', AuthConstants.MaxPasswordLength);

        var registerErrors = AuthValidation.ValidateRegister(new RegisterRequest("cliente@nettes.hn", hugePassword), out _);
        var loginErrors = AuthValidation.ValidateLogin(new LoginRequest("cliente@nettes.hn", hugePassword), out _);

        Assert.Contains("password", registerErrors.Keys);
        Assert.Contains("password", loginErrors.Keys);
    }

    [Fact]
    public async Task Me_sin_sesion_responde_401_problem_details_y_nunca_redirige_a_html()
    {
        using var factory = CreateHostFactory("Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal(AuthProblemTypes.Unauthenticated, problem.Type);
    }

    [Theory]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Origin", "https://sitio-malicioso.com")]
    [InlineData("Origin", "null")]
    [InlineData("Referer", "https://sitio-malicioso.com/trampa")]
    public async Task Post_desde_otro_sitio_se_bloquea_con_403_csrf_antes_de_tocar_la_base(string headerName, string headerValue)
    {
        using var factory = CreateHostFactory("Development");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "cliente@nettes.hn", password = "ClaveSegura123" }),
        };
        request.Headers.TryAddWithoutValidation(headerName, headerValue);

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal(AuthProblemTypes.CsrfRejected, problem.Type);
    }

    [Fact]
    public async Task Post_reenviado_por_nextjs_con_x_forwarded_host_y_mismo_origin_pasa_csrf()
    {
        using var factory = CreateHostFactory("Development", simulatedProxyIp: IPAddress.Loopback);
        using var client = factory.CreateClient();

        // Entradas inválidas a propósito para comprobar que pasa el filtro CSRF (400 validación, no 403 CSRF) sin abrir PostgreSQL.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "invalido", password = "" }),
        };
        request.Headers.Add("X-Forwarded-Host", "localhost:3000");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_sin_sesion_responde_401_antes_de_evaluar_csrf()
    {
        using var factory = CreateHostFactory("Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task En_desarrollo_openapi_documenta_las_rutas_y_ejemplos_de_autenticacion()
    {
        using var factory = CreateHostFactory("Development");
        using var client = factory.CreateClient();

        var openApiJson = await client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("/api/auth/register", openApiJson);
        Assert.Contains("/api/auth/login", openApiJson);
        Assert.Contains("/api/auth/logout", openApiJson);
        Assert.Contains("/api/auth/me", openApiJson);
        Assert.Contains("cliente@nettes.hn", openApiJson);
    }

    [Theory]
    [InlineData("10.0.0.15")]
    [InlineData("::ffff:10.0.0.15")]
    [InlineData("fd12:3456:789a::10")]
    [InlineData("127.0.0.1")]
    public async Task UseForwardedHeaders_confia_en_redes_privadas_y_loopback(string proxyIp)
    {
        await using var host = await CreateEchoHostAsync();
        var server = host.GetTestServer();

        var context = await server.SendAsync(ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(proxyIp);
            ctx.Request.Method = HttpMethods.Get;
            ctx.Request.Path = "/echo";
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
            ctx.Request.Headers["X-Forwarded-Host"] = "catalogo.nettes.hn";
            ctx.Request.Headers["X-Forwarded-For"] = "190.5.20.30, 10.0.0.2";
        }, Ct);

        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal("catalogo.nettes.hn", context.Request.Host.Value);
        Assert.Equal(IPAddress.Parse("190.5.20.30"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task UseForwardedHeaders_ignora_cabeceras_falsificadas_desde_ips_publicas()
    {
        await using var host = await CreateEchoHostAsync();
        var server = host.GetTestServer();
        var attackerPublicIp = IPAddress.Parse("203.0.113.77");

        var context = await server.SendAsync(ctx =>
        {
            ctx.Connection.RemoteIpAddress = attackerPublicIp;
            ctx.Request.Method = HttpMethods.Get;
            ctx.Request.Path = "/echo";
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
            ctx.Request.Headers["X-Forwarded-Host"] = "falso.com";
            ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        }, Ct);

        Assert.Equal("http", context.Request.Scheme);
        Assert.NotEqual("falso.com", context.Request.Host.Value);
        Assert.Equal(attackerPublicIp, context.Connection.RemoteIpAddress);
    }

    private static WebApplicationFactory<Program> CreateHostFactory(string environmentName, IPAddress? simulatedProxyIp = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environmentName);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=pruebas;Username=pruebas;Password=pruebas;Timeout=1");
            builder.ConfigureTestServices(services =>
            {
                services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
                if (simulatedProxyIp is not null)
                {
                    services.AddSingleton<IStartupFilter>(new SimulatedRemoteIpStartupFilter(simulatedProxyIp));
                }
            });
        });

    private sealed class SimulatedRemoteIpStartupFilter(IPAddress remoteIp) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                ctx.Connection.RemoteIpAddress ??= remoteIp;
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }

    private sealed class InMemoryXmlRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _elements.AsReadOnly();

        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }

    private static async Task<WebApplication> CreateEchoHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuth(builder.Environment);

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.MapGet("/echo", () => Results.Ok());
        await app.StartAsync(Ct);
        return app;
    }
}
