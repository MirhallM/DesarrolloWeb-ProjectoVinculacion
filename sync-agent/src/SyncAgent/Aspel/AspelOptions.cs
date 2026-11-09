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

    public sealed class RetryOptions
    {
        /// <summary>Intentos totales (el primero más los reintentos). 4 = 1 intento + 3 reintentos con las esperas por defecto.</summary>
        public int MaxAttempts { get; set; } = 4;

        /// <summary>Espera antes de cada reintento, en segundos. Si hay menos valores que reintentos, se repite el último.</summary>
        public int[] DelaysSeconds { get; set; } = [5, 15, 45];
    }

    /// <summary>Devuelve los errores de configuración, o una lista vacía si todo es válido.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (PriceList < 1) errors.Add("Aspel:PriceList debe ser 1 o mayor.");
        if (CommandTimeoutSeconds < 1) errors.Add("Aspel:CommandTimeoutSeconds debe ser 1 o mayor.");
        if (ConnectTimeoutSeconds < 1) errors.Add("Aspel:ConnectTimeoutSeconds debe ser 1 o mayor.");
        if (Retry.MaxAttempts < 1) errors.Add("Aspel:Retry:MaxAttempts debe ser 1 o mayor.");
        if (Retry.DelaysSeconds.Any(d => d < 0)) errors.Add("Aspel:Retry:DelaysSeconds no admite valores negativos.");
        return errors;
    }
}
