using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using SyncAgent.Aspel;
using SyncAgent.Resilience;

namespace SyncAgent.Publishing;

/// <summary>
/// Envía el inventario a la API (US-06): iniciar corrida → lotes → completar. Cada paso es idempotente
/// (el agente genera el id de la corrida, los lotes se reemplazan por secuencia y completar devuelve el mismo resumen),
/// así que los reintentos ante fallos transitorios no duplican nada.
/// </summary>
/// <remarks>La llave solo viaja en el encabezado de cada solicitud: nunca se registra ni se incluye en un mensaje.</remarks>
public sealed class HttpCatalogPublisher(
    HttpClient http,
    ApiOptions options,
    RetryPolicy retryPolicy,
    ILogger<HttpCatalogPublisher> logger,
    string agentVersion) : ICatalogPublisher, IApiConnectivityCheck
{
    public const string KeyHeader = "X-Sync-Agent-Key";

    /// <summary>Tiempo para avisar a la API que se abandona la corrida; es un intento de cortesía.</summary>
    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Tamaño máximo que se lee de una respuesta de error (podría ser una página HTML de un proxy).</summary>
    private const int MaxErrorBodyChars = 4096;

    private TimeSpan RequestTimeout => TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

    // ------------------------------------------------------------------ envío

    public async Task<PublishResult> PublishAsync(IReadOnlyList<AspelProduct> products, CancellationToken cancellationToken)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromMinutes(options.TotalTimeoutMinutes));
        var token = total.Token;

        var runId = Guid.NewGuid();
        var hash = InventoryHasher.Compute(products);
        var started = false;

        try
        {
            var start = await SendAsync("iniciar la corrida", HttpMethod.Post, "api/sync/runs",
                new StartSyncRunRequest(runId, products.Count, hash, agentVersion), SyncApiJsonContext.Default.StartSyncRunRequest,
                SyncApiJsonContext.Default.StartSyncRunResponse, RequestTimeout, token);

            if (start?.Status == SyncResponseStatuses.Unchanged)
            {
                logger.LogInformation("Inventario sin cambios desde la última sincronización: la API lo registró sin enviar productos.");
                return new PublishResult(runId, true, hash, null);
            }

            if (start?.Status != SyncResponseStatuses.Running)
            {
                throw new ApiPublishException("La API respondió al iniciar con un estado inesperado.", isTransient: false);
            }

            started = true;
            var batchSize = Math.Min(options.BatchSize, start.MaxBatchSize ?? options.BatchSize);
            var batchCount = (products.Count + batchSize - 1) / batchSize;
            logger.LogInformation("Corrida {RunId} iniciada: {Count} artículos en {Batches} lotes de hasta {Size}.",
                runId, products.Count, batchCount, batchSize);

            for (var sequence = 0; sequence < batchCount; sequence++)
            {
                // El lote se arma en el momento y se suelta después de enviarlo: unos 200 KB a la vez.
                var items = new List<SyncBatchItem>(batchSize);
                var end = Math.Min(products.Count, (sequence + 1) * batchSize);
                for (var i = sequence * batchSize; i < end; i++)
                {
                    var p = products[i];
                    items.Add(new SyncBatchItem(p.Code, p.Description, p.Stock, p.Status, p.Price));
                }

                await SendAsync($"enviar el lote {sequence + 1} de {batchCount}", HttpMethod.Post, $"api/sync/runs/{runId}/batches",
                    new SyncBatchRequest(sequence, items), SyncApiJsonContext.Default.SyncBatchRequest,
                    (JsonTypeInfo<object>?)null, RequestTimeout, token);
            }

            var summary = await SendAsync("completar la corrida", HttpMethod.Post, $"api/sync/runs/{runId}/complete",
                new CompleteSyncRunRequest(products.Count, batchCount), SyncApiJsonContext.Default.CompleteSyncRunRequest,
                SyncApiJsonContext.Default.SyncRunSummary, TimeSpan.FromSeconds(options.CompleteTimeoutSeconds), token);

            if (summary?.Status != SyncResponseStatuses.Succeeded)
            {
                throw new ApiPublishException("La API respondió al completar con un estado inesperado.", isTransient: false);
            }

            return new PublishResult(runId, false, hash, summary);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && total.IsCancellationRequested)
        {
            var message = $"El envío a la API superó el tiempo total de {options.TotalTimeoutMinutes} minutos.";
            if (started) await TryAbortAsync(runId, message);
            throw new ApiPublishException(message, isTransient: false);
        }
        catch (Exception ex) when (started)
        {
            // La corrida no se aplicó: se avisa a la API para que no quede en "running" hasta que se considere abandonada.
            // Si la API ya la marcó como fallida (p. ej. el freno de seguridad), abortar no cambia nada.
            await TryAbortAsync(runId, ex is ApiPublishException ? ex.Message : "Error inesperado del agente.");
            throw;
        }
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<object, SyncPingResponse>("comprobar la API", HttpMethod.Get, "api/sync/ping",
            body: null, bodyType: null, SyncApiJsonContext.Default.SyncPingResponse, RequestTimeout, cancellationToken);

        if (response is not { Ok: true })
        {
            throw new ApiPublishException("La API respondió a la comprobación con un contenido inesperado.", isTransient: false);
        }
    }

    private async Task TryAbortAsync(Guid runId, string reason)
    {
        try
        {
            using var timeout = new CancellationTokenSource(AbortTimeout);
            await SendOnceAsync(HttpMethod.Post, $"api/sync/runs/{runId}/abort", new AbortSyncRunRequest(reason),
                SyncApiJsonContext.Default.AbortSyncRunRequest, (JsonTypeInfo<object>?)null, timeout.Token, timeout.Token);
        }
        catch (Exception ex) when (ex is ApiPublishException or OperationCanceledException)
        {
            // La corrida quedará como abandonada en la siguiente ejecución; no es un error nuevo.
            logger.LogDebug("No se pudo avisar a la API que se abandona la corrida {RunId}.", runId);
        }
    }

    // ------------------------------------------------------------------ HTTP

    /// <summary>Una solicitud con reintentos ante fallos transitorios.</summary>
    private Task<TResponse?> SendAsync<TBody, TResponse>(
        string operation,
        HttpMethod method,
        string path,
        TBody? body,
        JsonTypeInfo<TBody>? bodyType,
        JsonTypeInfo<TResponse>? responseType,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            async (_, token) =>
            {
                using var perRequest = CancellationTokenSource.CreateLinkedTokenSource(token);
                perRequest.CancelAfter(timeout);
                try
                {
                    return await SendOnceAsync(method, path, body, bodyType, responseType, perRequest.Token, token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new ApiPublishException($"La API no respondió en {timeout.TotalSeconds:N0} s al {operation}.", isTransient: true);
                }
            },
            ApiPublishException.IsTransientError,
            (attempt, wait, ex) => logger.LogWarning(
                "Intento {Attempt} de {Operation} falló: {Error} Se reintenta en {Seconds} s.",
                attempt, operation, ex.Message, wait.TotalSeconds),
            cancellationToken);

    private async Task<TResponse?> SendOnceAsync<TBody, TResponse>(
        HttpMethod method,
        string path,
        TBody? body,
        JsonTypeInfo<TBody>? bodyType,
        JsonTypeInfo<TResponse>? responseType,
        CancellationToken requestToken,
        CancellationToken callerToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(options.BaseUri, path));
        request.Headers.TryAddWithoutValidation(KeyHeader, options.AgentKey);
        if (body is not null && bodyType is not null)
        {
            request.Content = JsonContent.Create(body, bodyType);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        }
        catch (HttpRequestException ex) when (!callerToken.IsCancellationRequested)
        {
            // Se usa la categoría del error y no ex.Message, para no depender de qué incluya el mensaje.
            throw new ApiPublishException($"No se pudo conectar con la API ({Describe(ex.HttpRequestError)}).", isTransient: true);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (responseType is null || response.StatusCode == HttpStatusCode.NoContent)
                {
                    return default;
                }

                try
                {
                    return await response.Content.ReadFromJsonAsync(responseType, requestToken);
                }
                catch (JsonException)
                {
                    throw new ApiPublishException("La API respondió con un contenido que no se pudo interpretar.", isTransient: false);
                }
            }

            throw await DescribeFailureAsync(response, requestToken);
        }
    }

    private static async Task<ApiPublishException> DescribeFailureAsync(HttpResponseMessage response, CancellationToken token)
    {
        var status = (int)response.StatusCode;
        var problem = await TryReadProblemAsync(response, token);
        var detail = problem?.Detail ?? problem?.Title;

        var (message, transient) = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                ("La API rechazó la llave del agente (401). Revise Api:AgentKey y Sync__AgentKeyHashes en Railway.", false),
            HttpStatusCode.Forbidden => ("La API rechazó la solicitud (403).", false),
            HttpStatusCode.NotFound =>
                ("La API no encontró la ruta o la corrida (404). Revise Api:BaseUrl.", false),
            HttpStatusCode.RequestEntityTooLarge => ("El lote es demasiado grande para la API (413). Baje Api:BatchSize.", false),
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => ($"La API está ocupada ({status}).", true),
            _ when status is >= 300 and < 400 =>
                ($"La API respondió con una redirección ({status}); no se sigue para no reenviar la llave. Revise Api:BaseUrl (https y el dominio exacto).", false),
            _ when status >= 500 => ($"Error de la API o del proxy ({status}).", true),
            _ => ($"La API rechazó la solicitud ({status}).", false),
        };

        if (!string.IsNullOrWhiteSpace(detail) && response.StatusCode != HttpStatusCode.Unauthorized)
        {
            message += " " + detail;
        }

        return new ApiPublishException(message, transient, status, problem?.Type);
    }

    private static async Task<ApiProblem?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken token)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return null; // p. ej. la página HTML de un proxy: no se copia al log
        }

        try
        {
            var text = await response.Content.ReadAsStringAsync(token);
            if (text.Length > MaxErrorBodyChars) return null;
            return JsonSerializer.Deserialize(text, SyncApiJsonContext.Default.ApiProblem);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    private static string Describe(HttpRequestError error) => error switch
    {
        HttpRequestError.NameResolutionError => "no se encontró el dominio; revise Api:BaseUrl y la conexión a Internet",
        HttpRequestError.ConnectionError => "no se pudo abrir la conexión",
        HttpRequestError.SecureConnectionError => "falló la conexión segura (TLS)",
        HttpRequestError.ProxyTunnelError => "falló el proxy de la red",
        HttpRequestError.ResponseEnded => "la conexión se cortó antes de recibir la respuesta",
        _ => "error de red",
    };
}
