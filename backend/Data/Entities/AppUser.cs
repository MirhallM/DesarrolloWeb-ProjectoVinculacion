using Microsoft.AspNetCore.Identity;

namespace backend.Data.Entities;

/// <summary>
/// Cuenta de usuario. Hereda todas las columnas de Identity
/// (correo, hash de contraseña, bloqueo, etc.) y agrega dos propias.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
