using Microsoft.Extensions.Configuration;
using SyncAgent.Resilience;

namespace SyncAgent.Publishing;

/// <summary>Sección <c>Api</c> de la configuración: a dónde y con qué llave se envía el catálogo.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Valor de ejemplo de <c>appsettings.Production.example.json</c>: significa "todavía sin llave".</summary>
    public const string Placeholder = "CAMBIAR";

    /// <summary>Las llaves de New-SyncAgentKey.ps1 miden 43 caracteres.</summary>
    public const int MinKeyLength = 32;

    public const int MinBatchSize = 100;
    public const int MaxBatchSize = 2000;

    /// <summary>URL pública del sitio (el frontend reenvía /api/* al backend). Por ejemplo https://&lt;dominio&gt;.up.railway.app</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Llave del agente. Nunca se escribe en logs, mensajes ni en el estado.</summary>
    public string AgentKey { get; set; } = string.Empty;

    /// <summary>Artículos por lote (unos 200 KB con 1000; el reenvío de Next.js trunca a partir de 10 MB).</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Tiempo máximo de cada solicitud (iniciar, lote, comprobar).</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Tiempo máximo de "completar". Si se corta, el reintento recibe el resumen ya confirmado.</summary>
    public int CompleteTimeoutSeconds { get; set; } = 120;

    /// <summary>Tiempo máximo de todo el envío. Menor que el límite de 10 min de la tarea programada.</summary>
    public int TotalTimeoutMinutes { get; set; } = 8;

    /// <summary>Intentos por solicitud ante fallos transitorios (red, 5xx, 429). Las esperas son 5, 15 y 45 s.</summary>
    public int MaxAttempts { get; set; } = 4;

    private string? _loadError;

    public static ApiOptions Load(IConfiguration configuration)
    {
        try
        {
            return configuration.GetSection(SectionName).Get<ApiOptions>() ?? new ApiOptions();
        }
        catch (InvalidOperationException)
        {
            // No se repite el mensaje del binder: podría incluir el valor.
            return new ApiOptions
            {
                _loadError = "La sección Api de la configuración tiene un valor con formato inválido (un texto donde se espera un número).",
            };
        }
    }

    /// <summary>¿Hay URL y llave reales? Si no, <c>--test</c> solo avisa y <c>--once</c> no arranca.</summary>
    public bool IsConfigured =>
        _loadError is null
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(AgentKey)
        && !string.Equals(AgentKey.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reintentos de cada solicitud, con las esperas de fábrica (5, 15 y 45 s).</summary>
    public RetryOptions Retry => new() { MaxAttempts = MaxAttempts };

    /// <summary>URL base con "/" al final, para resolver rutas relativas.</summary>
    public Uri BaseUri => new(BaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute);

    /// <summary>Errores de configuración. Nunca incluyen la llave.</summary>
    public IReadOnlyList<string> Validate()
    {
        if (_loadError is not null)
        {
            return [_loadError];
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            errors.Add("Falta Api:BaseUrl: la URL pública del sitio (https://<dominio>.up.railway.app).");
        }
        else if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri)
                 || uri.Scheme is not ("https" or "http")
                 || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                 || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add("Api:BaseUrl no es una URL válida (ejemplo: https://<dominio>.up.railway.app, sin ? ni #).");
        }
        else if (uri.Scheme == "http" && !uri.IsLoopback)
        {
            // La llave viaja en un encabezado: fuera de este equipo, solo por HTTPS.
            errors.Add("Api:BaseUrl debe usar https (http solo se admite con localhost o 127.0.0.1, para desarrollo).");
        }

        if (string.IsNullOrWhiteSpace(AgentKey) || string.Equals(AgentKey.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(@"Falta Api:AgentKey. Genérela con scripts\New-SyncAgentKey.ps1 -ConfigPath <appsettings.Production.json>.");
        }
        else if (AgentKey.Length < MinKeyLength || AgentKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            errors.Add($"Api:AgentKey no tiene el formato esperado (al menos {MinKeyLength} caracteres, sin espacios). Genérela con New-SyncAgentKey.ps1.");
        }

        if (BatchSize is < MinBatchSize or > MaxBatchSize)
        {
            errors.Add($"Api:BatchSize debe estar entre {MinBatchSize} y {MaxBatchSize}.");
        }

        if (RequestTimeoutSeconds < 1) errors.Add("Api:RequestTimeoutSeconds debe ser 1 o mayor.");
        if (CompleteTimeoutSeconds < 1) errors.Add("Api:CompleteTimeoutSeconds debe ser 1 o mayor.");
        if (TotalTimeoutMinutes < 1) errors.Add("Api:TotalTimeoutMinutes debe ser 1 o mayor.");
        if (MaxAttempts < 1) errors.Add("Api:MaxAttempts debe ser 1 o mayor.");

        return errors;
    }

    /// <summary>Descripción para logs y pantalla: solo la URL, nunca la llave.</summary>
    public string Describe() => string.IsNullOrWhiteSpace(BaseUrl) ? "(sin configurar)" : BaseUrl.Trim();
}
