using Microsoft.Data.SqlClient;

namespace SyncAgent.Diagnostics;

/// <summary>
/// Convierte una excepción en un texto seguro para el log: número de error, explicación en español y el
/// mensaje original con los secretos ocultos. Es lo único que debe registrarse de una <see cref="SqlException"/>.
/// </summary>
public sealed class SqlErrorDescriber(SecretRedactor redactor)
{
    public string Describe(Exception exception)
    {
        if (exception is SqlException sql)
        {
            var number = sql.Number;
            var detail = redactor.Redact(sql.Message);
            return $"Error de SQL Server {number} (clase {sql.Class}): {Explain(number)} Detalle: {detail}";
        }

        return $"{exception.GetType().Name}: {redactor.Redact(exception.Message)}";
    }

    /// <summary>Explicación breve en español de los errores más comunes el día de la instalación.</summary>
    public static string Explain(int number) => number switch
    {
        18456 => "Falló el inicio de sesión: usuario o contraseña incorrectos, o la cuenta está deshabilitada o bloqueada.",
        4060 => "El usuario no puede abrir la base indicada: revisar el nombre de la base y los permisos de la cuenta.",
        229 or 262 => "Permiso denegado: la cuenta no tiene SELECT sobre la tabla (revisar sql/install/01).",
        207 => "Nombre de columna no válido: la estructura de la tabla no coincide con lo esperado.",
        208 => "Objeto no válido: la tabla no existe en la base indicada (¿base equivocada?).",
        -2 => "Se agotó el tiempo de espera de la consulta o de la conexión.",
        53 or 40 or 258 or 10060 or 10061 or 11001 or 233 =>
            "No se pudo conectar al servidor: revisar que SQL Server esté encendido, el nombre o instancia, y que TCP/IP esté habilitado.",
        121 or 10053 or 10054 => "La conexión se interrumpió durante la operación.",
        1205 => "La consulta fue elegida como víctima de un bloqueo mutuo (deadlock).",
        _ => "Error no catalogado por el agente.",
    };
}
