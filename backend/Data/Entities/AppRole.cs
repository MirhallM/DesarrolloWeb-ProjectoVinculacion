using Microsoft.AspNetCore.Identity;

namespace backend.Data.Entities;

public class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
    }

    public AppRole(string roleName) : base(roleName)
    {
    }
}

/// <summary>
/// Nombres de los roles. Se usan en [Authorize(Roles = AppRoleNames.Admin)].
/// </summary>
public static class AppRoleNames
{
    public const string User = "User";
    public const string Admin = "Admin";
}
