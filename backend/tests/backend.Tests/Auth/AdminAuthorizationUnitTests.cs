using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Xml.Linq;
using backend.Auth;
using backend.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace backend.Tests.Auth;

public sealed class AdminAuthorizationUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AppPermissions_otorga_solo_favoritos_a_user_y_todos_los_privilegios_a_admin()
    {
        var userPerms = AppPermissions.GetForRoles([AppRoleNames.User]);
        var adminPerms = AppPermissions.GetForRoles([AppRoleNames.Admin]);
        var unknownPerms = AppPermissions.GetForRoles(["Invitado"]);

        Assert.Equal([AppPermissions.FavoritesManage], userPerms);
        Assert.Contains(AppPermissions.AdminAccess, adminPerms);
        Assert.Contains(AppPermissions.ProductImagesManage, adminPerms);
        Assert.Contains(AppPermissions.FeaturedProductsManage, adminPerms);
        Assert.Contains(AppPermissions.SiteContentManage, adminPerms);
        Assert.Contains(AppPermissions.FavoritesManage, adminPerms);
        Assert.Empty(unknownPerms);
    }

    [Theory]
    [InlineData(new[] { "admin" }, true)]
    [InlineData(new[] { "admin", "list" }, true)]
    [InlineData(new[] { "admin", "promote", "dueno@nettes.hn" }, true)]
    [InlineData(new[] { "Admin" }, false)]
    [InlineData(new[] { "seed" }, false)]
    [InlineData(new string[0], false)]
    public void AdminCommand_se_reconoce_solo_como_primer_argumento_exacto(string[] args, bool expected)
    {
        Assert.Equal(expected, AdminCommand.IsRequested(args));
    }

    [Theory]
    [InlineData((object)new[] { "admin" })]
    [InlineData((object)new[] { "admin", "promote" })]
    [InlineData((object)new[] { "admin", "create", "dueno@nettes.hn" })]
    [InlineData((object)new[] { "admin", "promote", "dueno@nettes.hn", "extra" })]
    public async Task AdminCommand_con_argumentos_invalidos_devuelve_codigo_2_sin_tocar_la_base(string[] args)
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var exitCode = await AdminCommand.RunAsync(services, args, Ct);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData("api/admin", true)]
    [InlineData("api/admin/images", true)]
    [InlineData("/api/admin/content", true)]
    [InlineData("api/products", false)]
    [InlineData("api/auth", false)]
    public void AdminRouteConvention_agrega_guard_automaticamente_solo_en_rutas_api_admin(string route, bool shouldProtect)
    {
        var convention = new AdminRouteConvention();
        var model = CreateControllerModel(typeof(DummyController), route);

        convention.Apply(model);

        Assert.Equal(shouldProtect, model.Filters.OfType<RequireAdminAttribute>().Any());
    }

    [Fact]
    public void AdminRouteConvention_impide_allow_anonymous_en_controladores_administrativos()
    {
        var convention = new AdminRouteConvention();
        var model = CreateControllerModel(typeof(AnonymousAdminDummyController), "api/admin/inseguro", [new AllowAnonymousAttribute()]);

        Assert.Throws<InvalidOperationException>(() => convention.Apply(model));
    }

    [Fact]
    public async Task Endpoint_admin_sin_sesion_responde_401_problem_details_sin_redirigir()
    {
        using var factory = CreateHostFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/admin/access", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal(AuthProblemTypes.Unauthenticated, problem.Type);
    }

    [Fact]
    public async Task Endpoint_admin_con_cookie_de_usuario_normal_responde_403_problem_details_sin_redirigir()
    {
        using var factory = CreateHostFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var userCookie = CreateProtectedAuthCookie(factory.Services, AppRoleNames.User);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/access");
        request.Headers.Add("Cookie", $"{AuthConstants.DevAuthCookieName}={userCookie}");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal(AuthProblemTypes.Forbidden, problem.Type);
    }

    [Fact]
    public async Task Endpoint_admin_con_administrador_activo_responde_200_y_devuelve_privilegios()
    {
        var adminId = Guid.NewGuid();
        var store = new InMemoryAdminUserStore(adminId, "admin@nettes.hn", isActive: true, [AppRoleNames.Admin, AppRoleNames.User]);
        using var factory = CreateHostFactory(store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var adminCookie = CreateProtectedAuthCookie(factory.Services, AppRoleNames.Admin, adminId, "admin@nettes.hn");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/access");
        request.Headers.Add("Cookie", $"{AuthConstants.DevAuthCookieName}={adminCookie}");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthUserResponse>(Ct);
        Assert.NotNull(body);
        Assert.Equal(adminId, body.Id);
        Assert.Equal("admin@nettes.hn", body.Email);
        Assert.Equal([AppRoleNames.Admin, AppRoleNames.User], body.Roles);
        Assert.Contains(AppPermissions.AdminAccess, body.Permissions);
        Assert.Contains(AppPermissions.ProductImagesManage, body.Permissions);
        Assert.Contains(AppPermissions.FeaturedProductsManage, body.Permissions);
        Assert.Contains(AppPermissions.SiteContentManage, body.Permissions);
    }

    [Fact]
    public async Task Endpoint_admin_rechaza_con_403_si_el_rol_admin_fue_revocado_y_con_401_si_fue_desactivado()
    {
        var userId = Guid.NewGuid();
        var store = new InMemoryAdminUserStore(userId, "exadmin@nettes.hn", isActive: true, [AppRoleNames.User]);
        using var factory = CreateHostFactory(store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // La cookie aún afirma ser Admin, pero en el almacén ya solo tiene rol User -> 403 inmediato.
        var staleAdminCookie = CreateProtectedAuthCookie(factory.Services, AppRoleNames.Admin, userId, "exadmin@nettes.hn");
        using (var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/admin/access"))
        {
            req1.Headers.Add("Cookie", $"{AuthConstants.DevAuthCookieName}={staleAdminCookie}");
            var resp1 = await client.SendAsync(req1, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, resp1.StatusCode);
        }

        // Si además la cuenta pasa a inactiva -> 401 inmediato.
        store.IsActive = false;
        using (var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/admin/access"))
        {
            req2.Headers.Add("Cookie", $"{AuthConstants.DevAuthCookieName}={staleAdminCookie}");
            var resp2 = await client.SendAsync(req2, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, resp2.StatusCode);
        }
    }

    [Fact]
    public async Task OpenApi_documenta_ruta_admin_y_no_expone_rutas_publicas_para_asignar_roles()
    {
        using var factory = CreateHostFactory();
        using var client = factory.CreateClient();

        var openApiJson = await client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("/api/admin/access", openApiJson);
        Assert.DoesNotContain("/api/auth/promote", openApiJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/admin/promote", openApiJson, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateProtectedAuthCookie(
        IServiceProvider services,
        string role,
        Guid? userId = null,
        string email = "usuario@nettes.hn")
    {
        var cookieOptions = services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim("AspNet.Identity.SecurityStamp", Guid.NewGuid().ToString("N")),
        ], IdentityConstants.ApplicationScheme);

        var now = DateTimeOffset.UtcNow;
        var properties = new AuthenticationProperties
        {
            IssuedUtc = now,
            ExpiresUtc = now.AddHours(1),
        };

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties, IdentityConstants.ApplicationScheme);
        return cookieOptions.TicketDataFormat.Protect(ticket);
    }

    private static ControllerModel CreateControllerModel(
        Type controllerType,
        string routeTemplate,
        IReadOnlyList<object>? extraAttributes = null)
    {
        var attributes = new List<object> { new RouteAttribute(routeTemplate) };
        if (extraAttributes is not null)
        {
            attributes.AddRange(extraAttributes);
        }

        var model = new ControllerModel(controllerType.GetTypeInfo(), attributes);
        model.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(routeTemplate)),
        });
        return model;
    }

    private static WebApplicationFactory<Program> CreateHostFactory(IUserStore<AppUser>? customUserStore = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=pruebas;Username=pruebas;Password=pruebas;Timeout=1");
            builder.ConfigureTestServices(services =>
            {
                services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
                if (customUserStore is not null)
                {
                    services.AddSingleton(customUserStore);
                }
            });
        });

    private sealed class InMemoryAdminUserStore(Guid id, string email, bool isActive, IReadOnlyList<string> roles)
        : IUserRoleStore<AppUser>
    {
        public bool IsActive { get; set; } = isActive;

        public void Dispose() { }

        public Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.TryParse(userId, out var parsed) && parsed == id
                ? new AppUser { Id = id, Email = email, UserName = email, IsActive = IsActive }
                : null);

        public Task<IList<string>> GetRolesAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult<IList<string>>(user.Id == id ? [.. roles] : []);

        public Task<bool> IsInRoleAsync(AppUser user, string roleName, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id == id && roles.Contains(roleName, StringComparer.OrdinalIgnoreCase));

        public Task<string> GetUserIdAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<AppUser?>(null);
        public Task AddToRoleAsync(AppUser user, string roleName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveFromRoleAsync(AppUser user, string roleName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IList<AppUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) => Task.FromResult<IList<AppUser>>([]);
    }

    private sealed class InMemoryXmlRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _elements.AsReadOnly();

        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }

    private sealed class DummyController : ControllerBase;

    private sealed class AnonymousAdminDummyController : ControllerBase;
}
