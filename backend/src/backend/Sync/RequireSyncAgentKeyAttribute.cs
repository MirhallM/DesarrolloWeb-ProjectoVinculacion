using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace backend.Sync;

/// <summary>
/// Solo deja pasar las solicitudes con la llave del agente en <see cref="SyncAgentKeyVerifier.HeaderName"/>.
/// Si falla responde 401 sin detalles. Se ejecuta antes de leer el cuerpo, así que una solicitud rechazada no cambia nada.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSyncAgentKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var verifier = context.HttpContext.RequestServices.GetRequiredService<SyncAgentKeyVerifier>();
        var values = context.HttpContext.Request.Headers[SyncAgentKeyVerifier.HeaderName];

        // Exactamente un valor; la llave nunca se registra.
        if (values.Count != 1 || !verifier.Verify(values[0]))
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
