using backend.Catalog;
using backend.Data;
using backend.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace backend.Sync;

public enum SyncResultKind
{
    Ok,
    Created,
    NoContent,
    BadRequest,
    NotFound,
    Conflict,
    Unprocessable,
}

/// <summary>Resultado de una operación de sincronización; el controlador lo traduce a HTTP.</summary>
public sealed record SyncResult(SyncResultKind Kind, object? Body = null, string? ProblemType = null, string? Title = null, string? Detail = null)
{
    public static SyncResult Ok(object body) => new(SyncResultKind.Ok, body);
    public static SyncResult Created(object body) => new(SyncResultKind.Created, body);
    public static SyncResult NoContent() => new(SyncResultKind.NoContent);

    public static SyncResult Problem(SyncResultKind kind, string type, string title, string detail) =>
        new(kind, null, type, title, detail);
}

/// <summary>
/// Protocolo de sincronización (US-06): iniciar corrida → lotes al área de preparación → completar en una transacción.
/// El catálogo solo cambia al completar, y solo si todo llegó y pasó el freno de seguridad.
/// </summary>
public sealed class SyncService(
    AppDbContext db,
    CatalogProductFields fields,
    IOptions<SyncOptions> options,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    ILogger<SyncService> logger)
{
    public const int MaxItemCount = 200_000;
    public const int MaxRejectedCodesInResponse = 50;
    public const int MaxReasonLength = 500;

    // Límites de estructura de un lote. Son holgados: los límites reales de las columnas se aplican al completar
    // y, si no se cumplen, el artículo se rechaza sin bloquear la corrida.
    private const int MaxRawCodeLength = 200;
    private const int MaxRawDescriptionLength = 1000;
    private const int MaxRawStatusLength = 20;

    private const string UniqueViolation = "23505";

    private SyncOptions Options => options.Value;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ iniciar

    public async Task<SyncResult> StartAsync(StartSyncRunRequest request, CancellationToken ct)
    {
        if (request.RunId == Guid.Empty)
        {
            return Invalid("runId es obligatorio.");
        }

        if (request.ItemCount is < 0 or > MaxItemCount)
        {
            return Invalid($"itemCount debe estar entre 0 y {MaxItemCount}.");
        }

        var hash = request.InventoryHash?.Trim().ToLowerInvariant();
        if (hash is not null && (hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
        {
            return Invalid("inventoryHash debe ser un SHA-256 en hexadecimal (64 caracteres).");
        }

        // Reintento del mismo inicio (p. ej. se perdió la respuesta): devuelve lo mismo sin crear nada.
        var existing = await db.SyncRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.RunId, ct);
        if (existing is not null)
        {
            return DescribeExisting(existing);
        }

        var now = Now;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await CleanUpAsync(now, ct);

        if (await db.SyncRuns.AnyAsync(r => r.Status == SyncRunStatuses.Running, ct))
        {
            await transaction.CommitAsync(ct); // conserva la limpieza
            return SyncResult.Problem(SyncResultKind.Conflict, SyncProblemTypes.RunInProgress,
                "Hay otra sincronización en curso",
                "Otra corrida sigue en ejecución. Esta se omite; la siguiente ejecución del agente lo intentará de nuevo.");
        }

        if (hash is not null && await IsUnchangedAsync(hash, request.ItemCount, now, ct))
        {
            db.SyncRuns.Add(new SyncRun
            {
                Id = request.RunId,
                Status = SyncRunStatuses.Succeeded,
                Mode = SyncRunModes.Unchanged,
                InventoryHash = hash,
                StartedAt = now,
                FinishedAt = now,
                ProductsRead = request.ItemCount,
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation("Sincronización {RunId}: inventario sin cambios ({Count} artículos).", request.RunId, request.ItemCount);
            return SyncResult.Ok(new StartSyncRunResponse(request.RunId, SyncResponseStatuses.Unchanged, null, now));
        }

        db.SyncRuns.Add(new SyncRun
        {
            Id = request.RunId,
            Status = SyncRunStatuses.Running,
            Mode = SyncRunModes.Full,
            InventoryHash = hash,
            StartedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Otra corrida se registró en el mismo instante: el índice único parcial sobre "running" la detuvo.
            return SyncResult.Problem(SyncResultKind.Conflict, SyncProblemTypes.RunInProgress,
                "Hay otra sincronización en curso", "Otra corrida empezó al mismo tiempo. Esta se omite.");
        }

        await transaction.CommitAsync(ct);
        logger.LogInformation("Sincronización {RunId} iniciada: {Count} artículos anunciados.", request.RunId, request.ItemCount);
        return SyncResult.Created(new StartSyncRunResponse(request.RunId, SyncResponseStatuses.Running, Options.MaxBatchSize, null));
    }

    private static SyncResult DescribeExisting(SyncRun run) => (run.Status, run.Mode) switch
    {
        (SyncRunStatuses.Running, _) =>
            SyncResult.Ok(new StartSyncRunResponse(run.Id, SyncResponseStatuses.Running, null, null)),
        (SyncRunStatuses.Succeeded, SyncRunModes.Unchanged) =>
            SyncResult.Ok(new StartSyncRunResponse(run.Id, SyncResponseStatuses.Unchanged, null, run.FinishedAt)),
        _ => NotActive(run),
    };

    /// <summary>
    /// Marca como fallidas las corridas abandonadas (liberan el índice único de "running") y borra las corridas
    /// "sin cambios" antiguas, para que sync_runs no crezca unas 96 filas al día.
    /// </summary>
    private async Task CleanUpAsync(DateTime now, CancellationToken ct)
    {
        var abandonCutoff = now.AddMinutes(-Options.AbandonAfterMinutes);
        var abandoned = await db.SyncRuns
            .Where(r => r.Status == SyncRunStatuses.Running && r.StartedAt < abandonCutoff)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (abandoned.Count > 0)
        {
            var message = $"Corrida abandonada: no se completó en {Options.AbandonAfterMinutes} minutos.";
            await db.SyncRuns
                .Where(r => abandoned.Contains(r.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, SyncRunStatuses.Failed)
                    .SetProperty(r => r.FinishedAt, now)
                    .SetProperty(r => r.ErrorMessage, message), ct);
            await db.SyncRunItems.Where(i => abandoned.Contains(i.RunId)).ExecuteDeleteAsync(ct);
            logger.LogWarning("Se marcaron como abandonadas {Count} corridas de sincronización.", abandoned.Count);
        }

        var retentionCutoff = now.AddDays(-Options.UnchangedRunRetentionDays);
        await db.SyncRuns
            .Where(r => r.Mode == SyncRunModes.Unchanged && r.FinishedAt < retentionCutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// La API, no el agente, decide si el inventario no cambió: compara con la última corrida completa aplicada.
    /// Si se restauró la base o se perdió el estado del agente, la huella no coincide y se envía todo.
    /// </summary>
    private async Task<bool> IsUnchangedAsync(string hash, int itemCount, DateTime now, CancellationToken ct)
    {
        var lastFull = await db.SyncRuns
            .AsNoTracking()
            .Where(r => r.Status == SyncRunStatuses.Succeeded && r.Mode == SyncRunModes.Full)
            .OrderByDescending(r => r.FinishedAt)
            .FirstOrDefaultAsync(ct);

        return lastFull is not null
               && lastFull.InventoryHash == hash
               && lastFull.ProductsRead == itemCount
               && lastFull.FinishedAt >= now.AddHours(-Options.FullRunMaxAgeHours);
    }

    // ------------------------------------------------------------------ lotes

    public async Task<SyncResult> StageBatchAsync(Guid runId, SyncBatchRequest request, CancellationToken ct)
    {
        var items = request.Items;
        if (request.Sequence < 0)
        {
            return Invalid("sequence debe ser 0 o mayor.");
        }

        if (items is null || items.Count == 0)
        {
            return Invalid("El lote no trae artículos.");
        }

        if (items.Count > Options.MaxBatchSize)
        {
            return Invalid($"El lote trae {items.Count} artículos; el máximo es {Options.MaxBatchSize}.");
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null || item.Code is null)
            {
                return Invalid($"El artículo {i} del lote no trae code.");
            }

            if (item.Code.Length > MaxRawCodeLength
                || item.Description?.Length > MaxRawDescriptionLength
                || item.Status?.Length > MaxRawStatusLength)
            {
                return Invalid($"El artículo {i} del lote trae textos demasiado largos.");
            }
        }

        var run = await db.SyncRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return RunNotFound(runId);
        }

        if (run.Status != SyncRunStatuses.Running)
        {
            return NotActive(run);
        }

        // Comparación ordinal: "  2050000117121" y "2050000117121" son artículos distintos.
        var duplicate = items.GroupBy(i => i.Code!, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            return await FailDuplicateAsync(runId, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Reenviar una secuencia la reemplaza: el agente puede reintentar un lote cuya respuesta se perdió.
        await db.SyncRunItems.Where(i => i.RunId == runId && i.Sequence == request.Sequence).ExecuteDeleteAsync(ct);

        db.SyncRunItems.AddRange(items.Select(i => new SyncRunItem
        {
            RunId = runId,
            Sequence = request.Sequence,
            Code = i.Code!,
            Description = i.Description,
            Stock = i.Stock,
            Status = i.Status,
            Price = i.Price,
        }));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await FailDuplicateAsync(runId, ct);
        }

        await transaction.CommitAsync(ct);
        return SyncResult.NoContent();
    }

    private async Task<SyncResult> FailDuplicateAsync(Guid runId, CancellationToken ct)
    {
        const string message = "Un código de artículo llegó repetido en la corrida.";
        await MarkFailedAsync(runId, message, ct);
        return SyncResult.Problem(SyncResultKind.BadRequest, SyncProblemTypes.DuplicateCode, "Código repetido", message);
    }

    // ------------------------------------------------------------------ completar

    /// <summary>
    /// Aplica la corrida en una sola transacción. A propósito NO recibe el token de la solicitud.
    /// </summary>
    /// <remarks>
    /// El agente llega por el reenvío de Next.js, que corta a los 30 s. Si la transacción usara
    /// HttpContext.RequestAborted, un corte del proxy la revertiría; cada reintento empezaría de cero y volvería a
    /// cortarse, y la primera carga nunca terminaría. Con el token de la aplicación, la transacción termina aunque el
    /// cliente se haya ido, y el reintento del agente espera el candado de la fila y recibe el resumen ya confirmado.
    /// Solo se interrumpe si la aplicación se está deteniendo.
    /// </remarks>
    public async Task<SyncResult> CompleteAsync(Guid runId, CompleteSyncRunRequest request)
    {
        var ct = lifetime.ApplicationStopping;

        if (request.ItemCount is < 0 or > MaxItemCount || request.BatchCount < 0)
        {
            return Invalid("itemCount o batchCount inválidos.");
        }

        try
        {
            return await ApplyAsync(runId, request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La transacción ya se revirtió (se libera al salir de ApplyAsync). Se deja constancia sin datos sensibles.
            logger.LogError(ex, "Error al aplicar la sincronización {RunId}.", runId);
            db.ChangeTracker.Clear();
            await MarkFailedAsync(runId, $"Error al aplicar la sincronización ({ex.GetType().Name}). No se cambió nada.", ct);
            throw;
        }
    }

    private async Task<SyncResult> ApplyAsync(Guid runId, CompleteSyncRunRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE: dos "completar" de la misma corrida (un reintento) no se aplican a la vez; el segundo espera.
        var run = await db.SyncRuns
            .FromSql($"SELECT * FROM sync_runs WHERE id = {runId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        if (run is null)
        {
            return RunNotFound(runId);
        }

        if (run.Status == SyncRunStatuses.Succeeded)
        {
            return SyncResult.Ok(Summarize(run, [])); // reintento después del éxito
        }

        if (run.Status != SyncRunStatuses.Running)
        {
            return NotActive(run);
        }

        var now = Now;
        var staged = await db.SyncRunItems.AsNoTracking().Where(i => i.RunId == runId).ToListAsync(ct);

        var sequences = staged.Select(i => i.Sequence).Distinct().Order().ToList();
        var contiguous = sequences.Count == 0 || (sequences[0] == 0 && sequences[^1] == sequences.Count - 1);
        if (staged.Count != request.ItemCount || sequences.Count != request.BatchCount || !contiguous)
        {
            var message = $"Faltan datos: se anunciaron {request.ItemCount} artículos en {request.BatchCount} lotes " +
                          $"y llegaron {staged.Count} en {sequences.Count}. No se cambió nada.";
            await FailInTransactionAsync(run, message, now, ct);
            await transaction.CommitAsync(ct);
            return SyncResult.Problem(SyncResultKind.Unprocessable, SyncProblemTypes.CountMismatch, "La corrida está incompleta", message);
        }

        var products = await db.Products.ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct);
        var received = staged.Select(i => i.Code).ToHashSet(StringComparer.Ordinal);
        var activeMissing = products.Values.Where(p => p.IsActive && !received.Contains(p.Code)).ToList();
        var currentlyActive = products.Values.Count(p => p.IsActive);

        var brake = SyncSafetyBrake.Evaluate(staged.Count, currentlyActive, activeMissing.Count, Options);
        if (brake.Blocked)
        {
            await FailInTransactionAsync(run, brake.Reason!, now, ct);
            await transaction.CommitAsync(ct);
            logger.LogWarning("Sincronización {RunId} detenida por el freno de seguridad: {Reason}", runId, brake.Reason);
            return SyncResult.Problem(SyncResultKind.Unprocessable, SyncProblemTypes.SafetyBrake, "Freno de seguridad", brake.Reason!);
        }

        var categoryIds = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Slug, c => c.Id, StringComparer.Ordinal, ct);
        int inserted = 0, updated = 0;
        var rejectedCodes = new List<string>();

        foreach (var item in staged)
        {
            var values = SyncItemRules.Evaluate(item.Code, item.Description, item.Stock, item.Status, item.Price);
            if (values.IsRejected)
            {
                // Si el producto ya existía no se toca: sí vino de Aspel, así que tampoco se desactiva.
                rejectedCodes.Add(item.Code);
                continue;
            }

            if (products.TryGetValue(item.Code, out var product))
            {
                if (ApplyChanges(product, values, categoryIds))
                {
                    product.SyncedAt = now; // solo si algo cambió: así tampoco cambia updated_at
                    updated++;
                }
            }
            else
            {
                db.Products.Add(new Product
                {
                    Id = Guid.NewGuid(),
                    Code = item.Code,
                    Description = values.Description,
                    DisplayName = fields.GetDisplayName(values.Description),
                    CategoryId = fields.GetCategoryId(values.Description, categoryIds),
                    PublicPrice = values.PublicPrice,
                    Stock = values.Stock,
                    IsActive = values.IsActive,
                    SyncedAt = now,
                });
                inserted++;
            }
        }

        // Los que dejaron de venir de Aspel no se borran (favoritos, imágenes): pasan a inactivos.
        foreach (var product in activeMissing)
        {
            product.IsActive = false;
            product.SyncedAt = now;
        }

        run.Status = SyncRunStatuses.Succeeded;
        run.FinishedAt = now;
        run.ProductsRead = staged.Count;
        run.ProductsInserted = inserted;
        run.ProductsUpdated = updated;
        run.ProductsDeactivated = activeMissing.Count;
        run.ProductsRejected = rejectedCodes.Count;
        run.ErrorMessage = null;

        await db.SaveChangesAsync(ct);
        await db.SyncRunItems.Where(i => i.RunId == runId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Sincronización {RunId} aplicada: {Read} leídos, {Inserted} insertados, {Updated} actualizados, " +
            "{Deactivated} desactivados, {Rejected} rechazados.",
            runId, run.ProductsRead, inserted, updated, run.ProductsDeactivated, rejectedCodes.Count);
        if (rejectedCodes.Count > 0)
        {
            // Los códigos no son datos sensibles y ayudan a corregir el artículo en Aspel.
            logger.LogWarning("Artículos rechazados en {RunId}: {Codes}", runId,
                string.Join(", ", rejectedCodes.Take(MaxRejectedCodesInResponse).Select(c => $"'{c}'")));
        }

        return SyncResult.Ok(Summarize(run, rejectedCodes.Take(MaxRejectedCodesInResponse).ToList()));
    }

    /// <summary>Copia los valores nuevos; devuelve true si algo cambió.</summary>
    private bool ApplyChanges(Product product, SyncItemValues values, IReadOnlyDictionary<string, Guid> categoryIds)
    {
        var changed = false;

        if (!string.Equals(product.Description, values.Description, StringComparison.Ordinal))
        {
            // Solo una descripción nueva recalcula nombre y categoría. Recalcular todo cuando cambian las reglas es US-07.
            product.Description = values.Description;
            product.DisplayName = fields.GetDisplayName(values.Description);
            product.CategoryId = fields.GetCategoryId(values.Description, categoryIds);
            changed = true;
        }

        if (product.PublicPrice != values.PublicPrice)
        {
            product.PublicPrice = values.PublicPrice;
            changed = true;
        }

        if (product.Stock != values.Stock)
        {
            product.Stock = values.Stock;
            changed = true;
        }

        if (product.IsActive != values.IsActive)
        {
            product.IsActive = values.IsActive;
            changed = true;
        }

        return changed;
    }

    private async Task FailInTransactionAsync(SyncRun run, string message, DateTime now, CancellationToken ct)
    {
        run.Status = SyncRunStatuses.Failed;
        run.FinishedAt = now;
        run.ErrorMessage = message;
        await db.SaveChangesAsync(ct);
        await db.SyncRunItems.Where(i => i.RunId == run.Id).ExecuteDeleteAsync(ct);
    }

    private static SyncRunSummary Summarize(SyncRun run, IReadOnlyList<string> rejectedCodes) => new(
        run.Id, SyncResponseStatuses.Succeeded, run.ProductsRead, run.ProductsInserted, run.ProductsUpdated,
        run.ProductsDeactivated, run.ProductsRejected, rejectedCodes, run.FinishedAt);

    // ------------------------------------------------------------------ abortar

    public async Task<SyncResult> AbortAsync(Guid runId, AbortSyncRunRequest request, CancellationToken ct)
    {
        var run = await db.SyncRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return RunNotFound(runId);
        }

        if (run.Status == SyncRunStatuses.Succeeded)
        {
            return NotActive(run);
        }

        if (run.Status == SyncRunStatuses.Running)
        {
            await MarkFailedAsync(runId, "Cancelada por el agente: " + Sanitize(request.Reason), ct);
        }

        return SyncResult.NoContent();
    }

    private static string Sanitize(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "sin motivo.";
        }

        var clean = new string(reason.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= MaxReasonLength ? clean : clean[..MaxReasonLength];
    }

    // ------------------------------------------------------------------ apoyo

    /// <summary>Marca la corrida como fallida (si sigue en curso) y borra su preparación, fuera de cualquier transacción.</summary>
    private async Task MarkFailedAsync(Guid runId, string message, CancellationToken ct)
    {
        var now = Now;
        await db.SyncRuns
            .Where(r => r.Id == runId && r.Status == SyncRunStatuses.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, SyncRunStatuses.Failed)
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.ErrorMessage, message), ct);
        await db.SyncRunItems.Where(i => i.RunId == runId).ExecuteDeleteAsync(ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };

    private static SyncResult Invalid(string detail) =>
        SyncResult.Problem(SyncResultKind.BadRequest, SyncProblemTypes.InvalidRequest, "Solicitud inválida", detail);

    private static SyncResult RunNotFound(Guid runId) =>
        SyncResult.Problem(SyncResultKind.NotFound, SyncProblemTypes.RunNotFound, "Corrida no encontrada",
            $"No existe la corrida {runId}.");

    private static SyncResult NotActive(SyncRun run) =>
        SyncResult.Problem(SyncResultKind.Conflict, SyncProblemTypes.RunNotActive, "La corrida ya no está activa",
            run.Status == SyncRunStatuses.Failed
                ? $"La corrida {run.Id} falló: {run.ErrorMessage}"
                : $"La corrida {run.Id} ya terminó ({run.Status}).");
}
