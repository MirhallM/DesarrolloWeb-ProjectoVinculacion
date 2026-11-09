using Microsoft.Extensions.Configuration;

namespace SyncAgent.Aspel;

/// <summary>Sección <c>Aspel</c> de la configuración.</summary>
public sealed class AspelOptions
{
    public const string SectionName = "Aspel";

    /// <summary>Lista de precios a leer (<c>CVE_PRECIO</c>). La regla comercial usa la 1.</summary>
    public int PriceList { get; set; } = 1;

    /// <summary>Tiempo máximo de la consulta. Pasado este límite se cancela y se reintenta.</summary>
    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>Tiempo máximo para abrir la conexión.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    public RetryOptions Retry { get; set; } = new();

    /// <summary>Clave de la lista de esperas dentro de la configuración.</summary>
    private const string DelaysKey = "Aspel:Retry:DelaysSeconds";

    /// <summary>Errores de lectura de la configuración (valores que no se pudieron interpretar).</summary>
    private readonly List<string> _loadErrors = [];

    /// <summary>
    /// Lee la sección <c>Aspel</c>.
    /// </summary>
    /// <remarks>
    /// Las esperas de fábrica (<c>[5,15,45]</c>) viven SOLO en el código; <c>appsettings.json</c> no las trae,
    /// para que no haya otra capa de configuración con la que mezclarlas por posición. El binder de .NET AGREGA
    /// los valores configurados a un arreglo que ya trae valores (<c>[5,15,45]</c> + <c>[1,2]</c> daría
    /// <c>[5,15,45,1,2]</c>), así que la lista se lee aparte y, si está definida, REEMPLAZA a la de fábrica.
    /// </remarks>
    public static AspelOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        AspelOptions options;
        try
        {
            options = section.Get<AspelOptions>() ?? new AspelOptions();
        }
        catch (InvalidOperationException)
        {
            // El binder no pudo convertir un valor (un texto donde se espera un número). No se repite su mensaje.
            var failed = new AspelOptions();
            failed._loadErrors.Add(
                "La sección Aspel de la configuración tiene un valor con formato inválido (un texto donde se espera un número). " +
                "Revise Aspel:Retry:DelaysSeconds, Aspel:PriceList y los demás valores numéricos.");
            return failed;
        }

        if (IsDefined(configuration))
        {
            options.Retry.DelaysSeconds = options.ReadDelays(configuration.GetSection(DelaysKey));
        }

        return options;
    }

    /// <summary>
    /// ¿Alguna fuente de configuración puso la lista de esperas? Un <c>[]</c> en el JSON no genera ninguna
    /// clave con hijos y <c>Exists()</c> da falso, pero el proveedor sí registra la clave (con valor nulo);
    /// por eso se pregunta a los proveedores. No decide precedencias: solo si alguien la definió.
    /// </summary>
    private static bool IsDefined(IConfiguration configuration) =>
        configuration is IConfigurationRoot root
            ? root.Providers.Any(p => p.TryGet(DelaysKey, out _) || p.GetChildKeys([], DelaysKey).Any())
            : configuration.GetSection(DelaysKey).Exists();

    private int[] ReadDelays(IConfigurationSection list)
    {
        var children = list.GetChildren().ToList();

        // Un valor suelto en vez de una lista (p. ej. la variable Aspel__Retry__DelaysSeconds=5).
        if (children.Count == 0 && !string.IsNullOrEmpty(list.Value))
        {
            _loadErrors.Add("Aspel:Retry:DelaysSeconds debe ser una lista de números (por ejemplo [5, 15, 45]), no un valor suelto.");
            return [];
        }

        // Los índices deben ser 0, 1, 2... sin huecos.
        var indexes = children.Select(c => int.TryParse(c.Key, out var i) ? i : -1).OrderBy(i => i).ToList();
        if (!indexes.SequenceEqual(Enumerable.Range(0, children.Count)))
        {
            _loadErrors.Add("Aspel:Retry:DelaysSeconds debe ser una lista sin huecos en las posiciones.");
            return [];
        }

        var values = new List<int>(children.Count);
        foreach (var child in children.OrderBy(c => int.Parse(c.Key)))
        {
            if (!int.TryParse(child.Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            {
                // No se repite el texto recibido: solo la posición.
                _loadErrors.Add($"Aspel:Retry:DelaysSeconds: el valor en la posición {child.Key} no es un número entero.");
                return [];
            }

            values.Add(seconds);
        }

        return [.. values];
    }

    public sealed class RetryOptions
    {
        /// <summary>Intentos totales (el primero más los reintentos). 4 = 1 intento + 3 reintentos con las esperas por defecto.</summary>
        public int MaxAttempts { get; set; } = 4;

        /// <summary>
        /// Espera antes de cada reintento, en segundos (de fábrica 5, 15 y 45). Si hay menos valores que reintentos,
        /// se repite el último. Si <c>appsettings.Production.json</c> la define, reemplaza a la de fábrica por completo.
        /// </summary>
        public int[] DelaysSeconds { get; set; } = [5, 15, 45];
    }

    /// <summary>Devuelve los errores de configuración, o una lista vacía si todo es válido.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>(_loadErrors);
        if (PriceList < 1) errors.Add("Aspel:PriceList debe ser 1 o mayor.");
        if (CommandTimeoutSeconds < 1) errors.Add("Aspel:CommandTimeoutSeconds debe ser 1 o mayor.");
        if (ConnectTimeoutSeconds < 1) errors.Add("Aspel:ConnectTimeoutSeconds debe ser 1 o mayor.");
        if (Retry.MaxAttempts < 1) errors.Add("Aspel:Retry:MaxAttempts debe ser 1 o mayor.");

        // Si ya hubo un error al leer la lista, no se agregan más mensajes sobre ella.
        if (_loadErrors.Count == 0)
        {
            if (Retry.DelaysSeconds.Length == 0)
            {
                errors.Add("Aspel:Retry:DelaysSeconds no puede estar vacía: indique al menos un valor en segundos, o quite la clave para usar los de fábrica (5, 15 y 45).");
            }

            var negatives = Retry.DelaysSeconds.Where(d => d < 0).ToList();
            if (negatives.Count > 0)
            {
                errors.Add($"Aspel:Retry:DelaysSeconds no admite valores negativos (recibidos: {string.Join(", ", negatives)}).");
            }
        }

        return errors;
    }
}
