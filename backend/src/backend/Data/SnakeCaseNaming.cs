using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

/// <summary>
/// Pone en snake_case los nombres de tablas, columnas, llaves e índices.
/// Las tablas de Identity pierden el prefijo "AspNet":
/// AspNetUsers -> users, AspNetUserRoles -> user_roles, etc.
/// Se llama al final de OnModelCreating, cuando el modelo ya está completo.
/// </summary>
internal static class SnakeCaseNaming
{
    private const string IdentityPrefix = "AspNet";

    public static void ApplySnakeCaseNames(this ModelBuilder builder)
    {
        // Las tablas de tipos "owned" las define su dueño.
        var entities = builder.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.GetTableName() is not null)
            .ToList();

        // 1) Primero todas las tablas, para que las llaves foráneas
        //    usen el nombre final de la tabla principal.
        foreach (var entity in entities)
        {
            var table = entity.GetTableName()!;

            if (table.StartsWith(IdentityPrefix, StringComparison.Ordinal))
            {
                entity.SetTableName(ToSnakeCase(table[IdentityPrefix.Length..]));
            }
        }

        // 2) Columnas, llave primaria, llaves foráneas e índices.
        foreach (var entity in entities)
        {
            var table = entity.GetTableName()!;

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.IsPrimaryKey()
                    ? $"pk_{table}"
                    : $"ak_{table}_{Columns(key.Properties.Select(p => p.Name))}");
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var principal = foreignKey.PrincipalEntityType.GetTableName();
                var columns = Columns(foreignKey.Properties.Select(p => p.Name));
                foreignKey.SetConstraintName($"fk_{table}_{principal}_{columns}");
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName($"ix_{table}_{Columns(index.Properties.Select(p => p.Name))}");
            }
        }
    }

    private static string Columns(IEnumerable<string> propertyNames) =>
        string.Join("_", propertyNames.Select(ToSnakeCase));

    // "NormalizedUserName" -> "normalized_user_name"
    private static string ToSnakeCase(string name) =>
        Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
}
