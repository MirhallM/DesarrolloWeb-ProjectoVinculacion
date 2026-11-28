using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using backend.Data;
using backend.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;

namespace backend.Auth;

/// <summary>Exige que <see cref="AppUser.IsActive"/> sea verdadero al iniciar sesión y al revalidar la cookie.</summary>
public sealed class AppSignInManager(
    UserManager<AppUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<AppUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<AppUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AppUser> confirmation)
    : SignInManager<AppUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(AppUser user) =>
        user.IsActive && await base.CanSignInAsync(user);

    public override async Task<AppUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is { IsActive: true } ? user : null;
    }
}

public static class AuthServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web);

    // Solo se aceptan cabeceras reenviadas desde loopback y redes privadas internas (IPv4, IPv6 ULA e IPv4 mapeado).
    private static readonly System.Net.IPNetwork[] TrustedProxyNetworks =
    [
        System.Net.IPNetwork.Parse("127.0.0.0/8"),
        System.Net.IPNetwork.Parse("::1/128"),
        System.Net.IPNetwork.Parse("::ffff:127.0.0.0/104"),
        System.Net.IPNetwork.Parse("10.0.0.0/8"),
        System.Net.IPNetwork.Parse("::ffff:10.0.0.0/104"),
        System.Net.IPNetwork.Parse("172.16.0.0/12"),
        System.Net.IPNetwork.Parse("::ffff:172.16.0.0/108"),
        System.Net.IPNetwork.Parse("192.168.0.0/16"),
        System.Net.IPNetwork.Parse("::ffff:192.168.0.0/112"),
        System.Net.IPNetwork.Parse("fc00::/7"),
        System.Net.IPNetwork.Parse("fe80::/10"),
    ];

    public static IServiceCollection AddAuth(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName);

        var isDevelopment = environment.IsDevelopment();
        var securePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 2;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var network in TrustedProxyNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        services
            .AddIdentityCore<AppUser>()
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager<AppSignInManager>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>()
            .AddPasswordValidator<LetterRequiredPasswordValidator>();

        services.AddOptions<IdentityOptions>().Configure<IOptions<AuthOptions>>((options, authOptions) =>
        {
            var auth = authOptions.Value;

            options.User.RequireUniqueEmail = true;

            options.Password.RequiredLength = AuthConstants.MinPasswordLength;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = AuthConstants.MinPasswordUniqueChars;

            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = Math.Max(1, auth.MaxFailedAccessAttempts);
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Max(1, auth.LockoutMinutes));

            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = false;
        });

        services.AddOptions<SecurityStampValidatorOptions>().Configure<IOptions<AuthOptions>>((options, authOptions) =>
        {
            var minutes = Math.Max(0, authOptions.Value.SecurityStampValidationMinutes);
            options.ValidationInterval = TimeSpan.FromMinutes(minutes);
        });

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<AuthOptions>>((options, authOptions) =>
            {
                var hours = Math.Max(1, authOptions.Value.SessionExpireHours);

                options.Cookie.Name = isDevelopment ? AuthConstants.DevAuthCookieName : AuthConstants.ProdAuthCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = securePolicy;
                options.Cookie.IsEssential = true;

                options.ExpireTimeSpan = TimeSpan.FromHours(hours);
                options.SlidingExpiration = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync,
                    OnRedirectToLogin = ctx => WriteProblemAsync(
                        ctx,
                        StatusCodes.Status401Unauthorized,
                        AuthProblemTypes.Unauthenticated,
                        "No autenticado",
                        "Debes iniciar sesión para acceder a este recurso."),
                    OnRedirectToAccessDenied = ctx => WriteProblemAsync(
                        ctx,
                        StatusCodes.Status403Forbidden,
                        AuthProblemTypes.Forbidden,
                        "Acceso denegado",
                        "No tienes permisos suficientes para realizar esta acción."),
                };
            });

        services.AddAuthorization();
        services.AddScoped<AdminRoleService>();

        return services;
    }

    public static OpenApiOptions AddAuthExamples(this OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            if (type == typeof(RegisterRequest))
            {
                schema.Examples =
                [
                    new JsonObject
                    {
                        ["email"] = "cliente@nettes.hn",
                        ["password"] = "ClaveDePrueba2026",
                    },
                ];
            }
            else if (type == typeof(LoginRequest))
            {
                schema.Examples =
                [
                    new JsonObject
                    {
                        ["email"] = "cliente@nettes.hn",
                        ["password"] = "ClaveDePrueba2026",
                        ["rememberMe"] = false,
                    },
                ];
            }
            else if (type == typeof(AuthUserResponse))
            {
                schema.Examples =
                [
                    new JsonObject
                    {
                        ["id"] = "01966b8a-0000-7000-8000-000000000001",
                        ["email"] = "cliente@nettes.hn",
                        ["roles"] = new JsonArray(AppRoleNames.User),
                        ["permissions"] = new JsonArray(AppPermissions.FavoritesManage),
                    },
                ];
            }

            return Task.CompletedTask;
        });

        return options;
    }

    private static async Task WriteProblemAsync(
        RedirectContext<CookieAuthenticationOptions> context,
        int statusCode,
        string problemType,
        string title,
        string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Type = problemType,
            Title = title,
            Detail = detail,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, ProblemJsonOptions));
    }
}
