using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace backend.Auth;

/// <summary>
/// Aplica automáticamente <see cref="RequireAdminAttribute"/> a todo controlador bajo <c>api/admin</c>
/// e impide exponer acciones anónimas en rutas administrativas.
/// </summary>
public sealed class AdminRouteConvention : IControllerModelConvention
{
    private const string AdminPrefix = "api/admin";

    public void Apply(ControllerModel controller)
    {
        if (!IsAdminController(controller))
        {
            return;
        }

        if (HasAllowAnonymous(controller.Attributes) || controller.Actions.Any(a => HasAllowAnonymous(a.Attributes)))
        {
            throw new InvalidOperationException(
                $"El controlador administrativo '{controller.ControllerType.Name}' no puede usar [AllowAnonymous].");
        }

        if (!controller.Filters.OfType<RequireAdminAttribute>().Any())
        {
            controller.Filters.Add(new RequireAdminAttribute());
        }
    }

    private static bool IsAdminController(ControllerModel controller) =>
        controller.Selectors.Any(s => IsAdminTemplate(s.AttributeRouteModel?.Template));

    private static bool IsAdminTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var normalized = template.Trim().TrimStart('/');
        return normalized.Equals(AdminPrefix, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(AdminPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllowAnonymous(IReadOnlyList<object> attributes) =>
        attributes.Any(a => a is IAllowAnonymous);
}
