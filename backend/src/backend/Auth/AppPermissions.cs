using backend.Data.Entities;

namespace backend.Auth;

/// <summary>
/// Privilegios de la aplicación derivados de los roles de Identity (<see cref="AppRoleNames"/>).
/// </summary>
public static class AppPermissions
{
    public const string FavoritesManage = "favorites:manage";
    public const string AdminAccess = "admin:access";
    public const string ProductImagesManage = "catalog:images:manage";
    public const string FeaturedProductsManage = "catalog:featured:manage";
    public const string SiteContentManage = "site:content:manage";

    public static IReadOnlyList<string> UserPermissions { get; } =
    [
        FavoritesManage,
    ];

    public static IReadOnlyList<string> AdminPermissions { get; } =
    [
        AdminAccess,
        FeaturedProductsManage,
        ProductImagesManage,
        FavoritesManage,
        SiteContentManage,
    ];

    public static IReadOnlyList<string> GetForRoles(IEnumerable<string> roles)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in roles)
        {
            if (string.Equals(role, AppRoleNames.Admin, StringComparison.OrdinalIgnoreCase))
            {
                set.UnionWith(AdminPermissions);
            }
            else if (string.Equals(role, AppRoleNames.User, StringComparison.OrdinalIgnoreCase))
            {
                set.UnionWith(UserPermissions);
            }
        }

        return [.. set.OrderBy(p => p, StringComparer.Ordinal)];
    }
}
