using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SyncAgent.Aspel;
using SyncAgent.Diagnostics;
using SyncAgent.Logging;
using SyncAgent.Publishing;
using SyncAgent.Resilience;

namespace SyncAgent.Tests.Unit;

/// <summary>El publicador HTTP contra un HttpMessageHandler falso: secuencia del protocolo, reintentos y la llave.</summary>
public sealed class HttpCatalogPublisherTests
{
    // Llave de prueba evidente.
    private const string Key = "llave-de-ejemplo-no-usar-0000000000000000000";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Dobles de prueba ----------

    private sealed record Call(HttpMethod Method, string Path, string? KeyHeader, string? Body);

    /// <summary>Responde según la ruta; guarda cada solicitud con su cuerpo y el encabezado de la llave.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        public Func<Call, int, HttpResponseMessage>? Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var key = request.Headers.TryGetValues(HttpCatalogPublisher.KeyHeader, out var values) ? values.Single() : null;
            var call = new Call(request.Method, request.RequestUri!.AbsolutePath, key, body);
            Calls.Add(call);
            var sameRoute = Calls.Count(c => c.Path == call.Path && c.Body == call.Body);
            return (Responder ?? DefaultResponder)(call, sameRoute);
        }

        public static HttpResponseMessage DefaultResponder(Call call, int attempt)
        {
            if (call.Path == "/api/sync/runs")
            {
                var runId = JsonDocument.Parse(call.Body!).RootElement.GetProperty("runId").GetGuid();
                return Json(HttpStatusCode.Created, new { runId, status = "running", maxBatchSize = 2000 });
            }

            if (call.Path.EndsWith("/batches")) return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (call.Path.EndsWith("/abort")) return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (call.Path.EndsWith("/complete"))
            {
                var count = JsonDocument.Parse(call.Body!).RootElement.GetProperty("itemCount").GetInt32();
                return Json(HttpStatusCode.OK, new
                {
                    runId = Guid.NewGuid(), status = "succeeded", read = count, inserted = count, updated = 0,
                    deactivated = 0, rejected = 0, rejectedCodes = Array.Empty<string>(),
                });
            }

            if (call.Path == "/api/sync/ping") return Json(HttpStatusCode.OK, new { ok = true, serverTimeUtc = DateTime.UtcNow });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, mediaType) };

    private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) =>
        Json(status, new { type, title = "x", detail, status = (int)status }, "application/problem+json");

    /// <summary>Registra todo lo que se escribiría en el log, ya formateado.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }

    private readonly FakeApi _api = new();
    private readonly CapturingLogger<HttpCatalogPublisher> _logger = new();
    private readonly List<TimeSpan> _waits = [];

    private HttpCatalogPublisher Publisher(Action<ApiOptions>? configure = null)
    {
        var options = new ApiOptions { BaseUrl = "https://tienda.example.test/", AgentKey = Key, BatchSize = 1000 };
        configure?.Invoke(options);
        var retry = new RetryPolicy(options.Retry, (wait, _) => { _waits.Add(wait); return Task.CompletedTask; });
        return new HttpCatalogPublisher(new HttpClient(_api), options, retry, _logger, "1.0.0-pruebas");
    }

    private static List<AspelProduct> Products(int count) =>
        Enumerable.Range(0, count).Select(i => new AspelProduct($"C{i:D5}", "LECHE ENT 1LT", i, "A", 10.5 + i)).ToList();

    // ---------- Pruebas ----------

    [Fact]
    public async Task Envia_iniciar_lotes_y_completar_con_la_llave_en_cada_solicitud()
    {
        var products = Products(2500);
        products[0] = new AspelProduct("  2050000117121", null, -2.5, "B", null); // exacto, sin recortar

        var result = await Publisher().PublishAsync(products, Ct);

        Assert.False(result.Unchanged);
        Assert.Equal(2500, result.Summary!.Inserted);
        Assert.Equal(
            ["/api/sync/runs", "batches", "batches", "batches", "complete"],
            _api.Calls.Select(c => c.Path == "/api/sync/runs" ? c.Path : c.Path.Split('/')[^1]).ToArray());
        Assert.All(_api.Calls, c => Assert.Equal(Key, c.KeyHeader));

        var sequences = _api.Calls.Where(c => c.Path.EndsWith("/batches"))
            .Select(c => JsonDocument.Parse(c.Body!).RootElement)
            .Select(b => (b.GetProperty("sequence").GetInt32(), b.GetProperty("items").GetArrayLength()))
            .ToArray();
        Assert.Equal([(0, 1000), (1, 1000), (2, 500)], sequences);

        var first = JsonDocument.Parse(_api.Calls[1].Body!).RootElement.GetProperty("items")[0];
        Assert.Equal("  2050000117121", first.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("price").ValueKind);

        var complete = JsonDocument.Parse(_api.Calls[^1].Body!).RootElement;
        Assert.Equal(2500, complete.GetProperty("itemCount").GetInt32());
        Assert.Equal(3, complete.GetProperty("batchCount").GetInt32());

        var start = JsonDocument.Parse(_api.Calls[0].Body!).RootElement;
        Assert.Equal(InventoryHasher.Compute(products), start.GetProperty("inventoryHash").GetString());
        Assert.Equal(result.InventoryHash, start.GetProperty("inventoryHash").GetString());
    }

    [Fact]
    public async Task Respeta_el_maximo_de_lote_que_anuncia_la_api()
    {
        _api.Responder = (call, attempt) => call.Path == "/api/sync/runs"
            ? Json(HttpStatusCode.Created, new { runId = Guid.NewGuid(), status = "running", maxBatchSize = 300 })
            : FakeApi.DefaultResponder(call, attempt);

        await Publisher().PublishAsync(Products(700), Ct);

        Assert.Equal(3, _api.Calls.Count(c => c.Path.EndsWith("/batches")));
    }

    [Fact]
    public async Task Si_la_api_dice_sin_cambios_no_envia_lotes()
    {
        _api.Responder = (call, _) => Json(HttpStatusCode.OK, new { runId = Guid.NewGuid(), status = "unchanged", finishedAt = DateTime.UtcNow });

        var result = await Publisher().PublishAsync(Products(50), Ct);

        Assert.True(result.Unchanged);
        Assert.Null(result.Summary);
        Assert.Single(_api.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Reintenta_los_errores_transitorios(HttpStatusCode transient)
    {
        _api.Responder = (call, attempt) => call.Path.EndsWith("/batches") && attempt == 1
            ? new HttpResponseMessage(transient)
            : FakeApi.DefaultResponder(call, attempt);

        var result = await Publisher().PublishAsync(Products(10), Ct);

        Assert.Equal(10, result.Summary!.Inserted);
        Assert.Equal(2, _api.Calls.Count(c => c.Path.EndsWith("/batches")));
        Assert.Equal([TimeSpan.FromSeconds(5)], _waits);
    }

    [Fact]
    public async Task Reintenta_si_no_hay_conexion()
    {
        var failures = 0;
        _api.Responder = (call, attempt) => call.Path == "/api/sync/runs" && failures++ < 2
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "no hay red")
            : FakeApi.DefaultResponder(call, attempt);

        await Publisher().PublishAsync(Products(5), Ct);

        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)], _waits);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "llave")]
    [InlineData(HttpStatusCode.BadRequest, "rechazó")]
    [InlineData(HttpStatusCode.NotFound, "Api:BaseUrl")]
    public async Task No_reintenta_los_errores_permanentes(HttpStatusCode status, string expected)
    {
        _api.Responder = (call, attempt) => call.Path == "/api/sync/runs" ? new HttpResponseMessage(status) : FakeApi.DefaultResponder(call, attempt);

        var ex = await Assert.ThrowsAsync<ApiPublishException>(() => Publisher().PublishAsync(Products(5), Ct));

        Assert.Contains(expected, ex.Message);
        Assert.Equal((int)status, ex.StatusCode);
        Assert.Single(_api.Calls);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task El_freno_de_seguridad_se_informa_con_el_detalle_de_la_api()
    {
        _api.Responder = (call, attempt) => call.Path.EndsWith("/complete")
            ? Problem(HttpStatusCode.UnprocessableEntity, "sync-safety-brake", "Freno de seguridad: se desactivarían 35 de 40 productos activos.")
            : FakeApi.DefaultResponder(call, attempt);

        var ex = await Assert.ThrowsAsync<ApiPublishException>(() => Publisher().PublishAsync(Products(5), Ct));

        Assert.Equal("sync-safety-brake", ex.ProblemType);
        Assert.Contains("35 de 40", ex.Message);
        Assert.Single(_api.Calls, c => c.Path.EndsWith("/complete")); // no se reintenta
    }

    [Fact]
    public async Task Si_completar_se_corta_el_reintento_recibe_el_resumen()
    {
        // El proxy de Next.js corta a los 30 s; la transacción sigue y el reintento recibe el resumen confirmado.
        _api.Responder = (call, attempt) => call.Path.EndsWith("/complete") && attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
            : FakeApi.DefaultResponder(call, attempt);

        var result = await Publisher().PublishAsync(Products(5), Ct);

        Assert.Equal(5, result.Summary!.Inserted);
        Assert.Equal(2, _api.Calls.Count(c => c.Path.EndsWith("/complete")));
    }

    [Fact]
    public async Task Un_timeout_de_la_solicitud_se_reintenta()
    {
        var slow = new SlowOnceApi();
        var options = new ApiOptions { BaseUrl = "https://tienda.example.test", AgentKey = Key, RequestTimeoutSeconds = 1 };
        var publisher = new HttpCatalogPublisher(new HttpClient(slow), options,
            new RetryPolicy(options.Retry, (_, _) => Task.CompletedTask), _logger, "1.0.0");

        await publisher.PingAsync(Ct);

        Assert.Equal(2, slow.Calls);
        Assert.Contains(_logger.Lines, l => l.Contains("no respondió"));
    }

    private sealed class SlowOnceApi : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++Calls == 1) await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Json(HttpStatusCode.OK, new { ok = true, serverTimeUtc = DateTime.UtcNow });
        }
    }

    [Fact]
    public async Task Un_error_en_un_lote_abandona_la_corrida_en_la_api()
    {
        _api.Responder = (call, attempt) => call.Path.EndsWith("/batches")
            ? Problem(HttpStatusCode.BadRequest, "sync-duplicate-code", "Un código de artículo llegó repetido en la corrida.")
            : FakeApi.DefaultResponder(call, attempt);

        await Assert.ThrowsAsync<ApiPublishException>(() => Publisher().PublishAsync(Products(5), Ct));

        var abort = Assert.Single(_api.Calls, c => c.Path.EndsWith("/abort"));
        Assert.Contains("repetido", abort.Body);
        Assert.Equal(Key, abort.KeyHeader);
    }

    [Fact]
    public async Task Una_redireccion_no_se_sigue()
    {
        _api.Responder = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("https://otro-sitio.example.test/api/sync/ping");
            return response;
        };
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        Assert.False(handler.AllowAutoRedirect); // el cliente real del agente (Program.CreateApiHttpClient) usa este ajuste

        var ex = await Assert.ThrowsAsync<ApiPublishException>(() => Publisher().PingAsync(Ct));

        Assert.Contains("redirección", ex.Message);
        Assert.Single(_api.Calls);
    }

    [Fact]
    public async Task Ping_correcto_no_envia_productos()
    {
        await Publisher().PingAsync(Ct);

        var call = Assert.Single(_api.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("/api/sync/ping", call.Path);
        Assert.Null(call.Body);
    }

    [Fact]
    public async Task La_llave_no_aparece_en_mensajes_ni_logs_aunque_la_api_la_repita()
    {
        // Una respuesta hostil que repite la llave: el 401 no copia el detalle y el redactor es la última defensa.
        _api.Responder = (_, _) => Problem(HttpStatusCode.Unauthorized, "x", $"llave recibida: {Key}");

        var ex = await Assert.ThrowsAsync<ApiPublishException>(() => Publisher().PublishAsync(Products(3), Ct));

        Assert.DoesNotContain(Key, ex.Message);
        Assert.DoesNotContain(Key, ex.ToString());
        Assert.All(_logger.Lines, l => Assert.DoesNotContain(Key, l));

        var redactor = SecretRedactor.None.With(Key);
        Assert.DoesNotContain(Key, LogLineFormatter.Format(DateTime.Now, LogLevel.Error, "x", $"texto con {Key}", null, redactor));
    }

    [Fact]
    public async Task Una_pagina_html_de_error_no_se_copia_al_mensaje()
    {
        _api.Responder = (_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html><body>Application failed to respond</body></html>", Encoding.UTF8, "text/html"),
        };

        var ex = await Assert.ThrowsAsync<ApiPublishException>(() => Publisher(o => o.MaxAttempts = 1).PingAsync(Ct));

        Assert.DoesNotContain("<html>", ex.Message);
        Assert.Contains("502", ex.Message);
    }
}
