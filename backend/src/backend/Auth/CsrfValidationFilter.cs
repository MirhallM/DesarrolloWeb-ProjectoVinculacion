using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace backend.Auth;

/// <summary>
/// Protege contra CSRF en métodos que modifican estado verificando los encabezados del navegador
/// (<c>Sec-Fetch-Site</c>, <c>Origin</c> y <c>Referer</c>), sin requerir cookies ni tokens adicionales.
/// </summary>
public sealed class CsrfValidationFilter : IAuthorizationFilter, IOrderedFilter
{
    // Se ejecuta después de [Authorize] (Order = 0) para devolver 401 antes de evaluar CSRF.
    public int Order => 1000;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        var method = request.Method;

        if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method))
        {
            return;
        }

        if (context.Filters.OfType<IgnoreAntiforgeryTokenAttribute>().Any()
            || context.ActionDescriptor.EndpointMetadata.OfType<IgnoreAntiforgeryTokenAttribute>().Any())
        {
            return;
        }

        // 1. Fetch Metadata (enviado automáticamente por navegadores modernos; no falsificable desde JS).
        var secFetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (!string.IsNullOrEmpty(secFetchSite)
            && !string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            Reject(context);
            return;
        }

        // 2. Verificación de Origin (o Referer si un navegador antiguo omite Origin).
        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin))
        {
            if (!MatchesRequestHost(origin, request.Host))
            {
                Reject(context);
            }

            return;
        }

        var referer = request.Headers.Referer.ToString();
        if (!string.IsNullOrEmpty(referer) && !MatchesRequestHost(referer, request.Host))
        {
            Reject(context);
        }
    }

    private static bool MatchesRequestHost(string rawUri, HostString expectedHost)
    {
        if (!expectedHost.HasValue || !Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var originAuthority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return string.Equals(originAuthority, expectedHost.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static void Reject(AuthorizationFilterContext context)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Type = AuthProblemTypes.CsrfRejected,
            Title = "Solicitud entre sitios bloqueada",
            Detail = "El origen de la solicitud no coincide con el sitio de la aplicación.",
        };

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
    }
}
