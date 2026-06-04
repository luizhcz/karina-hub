using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Ingestion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint da fila standalone — <c>POST</c> enfileira um workflow pra
/// execução assíncrona; <c>GET</c> retorna estado pra polling com ETag.
///
/// O caminho é mutuamente exclusivo do Chat Path: chat continua via SSE com
/// seu próprio slot counter (scope <c>chat</c>); standalone usa o slot
/// counter <c>standalone</c> consumido pelo <c>StandaloneJobDispatcherService</c>
/// no <c>Host.Worker</c>.
///
/// Feature flag <c>StandalonePools:Enabled</c> guarda os dois endpoints —
/// 503 quando desligado.
///
/// Multi-tenant: cada job grava <c>(TenantId, ProjectId)</c> resolvidos dos
/// accessors. <c>GET</c> recusa (404) jobs de outro projeto/tenant — mesmo
/// quando o caller conhece o JobId.
/// </summary>
[ApiController]
[Route("api/aihub/responses")]
public sealed class StandaloneResponsesController : ControllerBase
{
    private static readonly Regex IdempotencyKeyPattern =
        new("^[A-Za-z0-9_:.-]{1,128}$", RegexOptions.Compiled);

    private readonly IBackgroundResponseRepository _jobs;
    private readonly IWebhookDeliveryRepository _deliveries;
    private readonly StandalonePoolsOptions _options;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IUserContextAccessor _userAccessor;
    private readonly ILogger<StandaloneResponsesController> _logger;

    public StandaloneResponsesController(
        IBackgroundResponseRepository jobs,
        IWebhookDeliveryRepository deliveries,
        IOptions<StandalonePoolsOptions> options,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        IUserContextAccessor userAccessor,
        ILogger<StandaloneResponsesController> logger)
    {
        _jobs = jobs;
        _deliveries = deliveries;
        _options = options.Value;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _userAccessor = userAccessor;
        _logger = logger;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Enfileira execução assíncrona de um workflow standalone. Retorna 202 + jobId pra polling em GET /responses/{jobId}.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Enqueue(
        [FromBody] CreateStandaloneResponseRequest request,
        CancellationToken ct)
    {
        if (!_options.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Pool standalone desabilitado." });

        if (request is null || string.IsNullOrWhiteSpace(request.WorkflowId))
            return BadRequest(new { error = "workflowId obrigatório." });

        if (string.IsNullOrWhiteSpace(request.Input))
            return BadRequest(new { error = "input obrigatório — payload JSON consumido pelo workflow." });

        string? idempotencyKey;
        try
        {
            idempotencyKey = ResolveIdempotencyKey(request.IdempotencyKey);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        if (idempotencyKey is not null)
        {
            var existing = await _jobs.GetByIdempotencyKeyAsync(idempotencyKey, ct);
            if (existing is not null && BelongsToCurrentScope(existing))
                return Accepted(BuildLocation(existing.JobId), ToResponse(existing));
            // Idempotency-Key conflitando entre tenants: tratamos como conflito
            // genérico em vez de retornar o job alheio.
            if (existing is not null)
                return Conflict(new { error = "Idempotency-Key já em uso por outro tenant." });
        }

        var job = new BackgroundResponseJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            WorkflowId = request.WorkflowId,
            AgentId = string.Empty, // não usado pelo caminho standalone; manter constraint NOT NULL legacy.
            Input = request.Input,
            CallbackTarget = request.Callback,
            IdempotencyKey = idempotencyKey,
            Status = BackgroundResponseStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ProjectId = ResolveProjectId(),
            TenantId = ResolveTenantId(),
        };

        try
        {
            await _jobs.InsertAsync(job, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race: outra requisição criou o job com a mesma idempotencyKey em paralelo.
            if (idempotencyKey is not null)
            {
                var existing = await _jobs.GetByIdempotencyKeyAsync(idempotencyKey, ct);
                if (existing is not null && BelongsToCurrentScope(existing))
                    return Accepted(BuildLocation(existing.JobId), ToResponse(existing));
            }
            return Conflict(new { error = "Conflito ao gravar o job." });
        }

        _logger.LogInformation(
            "[StandaloneResponses] Job {JobId} enfileirado workflow={Wf} tenant={Tenant} project={Project} idem={Idem}.",
            job.JobId, job.WorkflowId, job.TenantId, job.ProjectId, idempotencyKey ?? "<none>");

        MetricsRegistry.StandaloneJobsEnqueued.Add(1,
            new KeyValuePair<string, object?>("project_id", job.ProjectId),
            new KeyValuePair<string, object?>("source", "responses"));

        return Accepted(BuildLocation(job.JobId), ToResponse(job));
    }

    [HttpGet("{jobId}")]
    [SwaggerOperation(Summary = "Lê o estado de um job standalone. Suporta If-None-Match → 304 quando inalterado desde o último GET.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(string jobId, CancellationToken ct)
    {
        if (!_options.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Pool standalone desabilitado." });

        var job = await _jobs.GetAsync(jobId, ct);
        // 404 quando o job não existe OU não pertence ao tenant/projeto atual.
        // Não diferenciamos entre "não existe" e "existe em outro tenant" pra
        // não vazar enumeração cross-tenant.
        if (job is null || !BelongsToCurrentScope(job)) return NotFound();

        var etag = BuildETag(job);

        if (Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var inmHeader)
            && IsNoneMatch(inmHeader, etag))
        {
            Response.Headers.ETag = etag;
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "no-cache, private";
        return Ok(ToResponse(job));
    }

    [HttpGet("{jobId}/deliveries")]
    [SwaggerOperation(Summary = "Histórico de tentativas de webhook do job. Admin-only — debug de delivery falhada.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListDeliveries(string jobId, CancellationToken ct)
    {
        // Defense-in-depth: o AdminGate global já filtra non-admin (rota fora
        // da whitelist), mas re-checamos aqui pra que qualquer ampliação
        // futura do regex do gate (ex.: liberar mais sub-rotas de /responses)
        // não vaze histórico de webhook com Url + LastError.
        if (_userAccessor.Current?.IsAdmin != true)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Acesso negado. Histórico de webhook é admin-only." });

        var job = await _jobs.GetAsync(jobId, ct);
        if (job is null || !BelongsToCurrentScope(job)) return NotFound();

        var list = await _deliveries.ListByJobAsync(jobId, ct);
        return Ok(list.Select(d => new DeliveryItemResponse
        {
            DeliveryId = d.DeliveryId,
            Url = d.Url,
            Status = d.Status.ToString(),
            LastResponseCode = d.LastResponseCode,
            LastError = d.LastError,
            DeliveredAt = d.DeliveredAt,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt,
        }).ToList());
    }

    // ETag opaco: aspas + Status + ":" + UpdatedAt em ticks (invariant). Cliente
    // reenvia em If-None-Match; comparação semântica via EntityTagHeaderValue
    // cobre wildcards, weak/strong e múltiplos valores RFC 9110.
    private static string BuildETag(BackgroundResponseJob j) =>
        $"\"{j.Status}:{j.UpdatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}\"";

    private static bool IsNoneMatch(Microsoft.Extensions.Primitives.StringValues inmHeader, string etag)
    {
        if (!EntityTagHeaderValue.TryParseList(inmHeader, out var clientTags) || clientTags is null)
            return false;

        var serverTag = new EntityTagHeaderValue(etag);
        foreach (var tag in clientTags)
        {
            // Wildcard: cliente pede 304 se cache de qualquer versão existir.
            if (tag.Equals(EntityTagHeaderValue.Any)) return true;
            // RFC 9110: If-None-Match usa weak comparison — strong/weak na mesma
            // tag combinam. Mas como o servidor só emite strong, basta comparar
            // a tag exata (ignorando o flag IsWeak do cliente, que pode vir W/...).
            if (string.Equals(tag.Tag.ToString(), serverTag.Tag.ToString(), StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private string BuildLocation(string jobId) => Url.Action(nameof(Get), new { jobId }) ?? $"/api/aihub/responses/{jobId}";

    private string ResolveProjectId()
    {
        var p = _projectAccessor.Current.ProjectId;
        return string.IsNullOrWhiteSpace(p) ? "default" : p;
    }

    private string ResolveTenantId()
    {
        var t = _tenantAccessor.Current.TenantId;
        return string.IsNullOrWhiteSpace(t) ? "default" : t;
    }

    private bool BelongsToCurrentScope(BackgroundResponseJob job) =>
        string.Equals(job.TenantId, ResolveTenantId(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(job.ProjectId, ResolveProjectId(), StringComparison.OrdinalIgnoreCase);

    private string? ResolveIdempotencyKey(string? bodyKey)
    {
        var headerRaw = Request.Headers.TryGetValue("Idempotency-Key", out var h) && !string.IsNullOrWhiteSpace(h)
            ? h.ToString().Trim()
            : null;
        var bodyRaw = string.IsNullOrWhiteSpace(bodyKey) ? null : bodyKey.Trim();

        if (bodyRaw is not null && headerRaw is not null
            && !string.Equals(bodyRaw, headerRaw, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Idempotency-Key divergente entre body e header. Envie em apenas um lugar ou mantenha valores idênticos.");
        }

        var key = bodyRaw ?? headerRaw;
        if (key is null) return null;

        if (!IdempotencyKeyPattern.IsMatch(key))
        {
            throw new InvalidOperationException(
                "Idempotency-Key inválida: até 128 chars no charset [A-Za-z0-9_:.-].");
        }

        return key;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql sqlstate 23505 = unique_violation.
        if (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505") return true;
        return false;
    }

    private static StandaloneResponse ToResponse(BackgroundResponseJob j) => new()
    {
        JobId = j.JobId,
        WorkflowId = j.WorkflowId,
        ExecutionId = j.ExecutionId,
        Status = j.Status.ToString(),
        Step = j.Step,
        Attempt = j.Attempt,
        Output = j.Status == BackgroundResponseStatus.Completed ? j.Output : null,
        LastError = j.Status == BackgroundResponseStatus.Failed ? j.LastError : null,
        CreatedAt = j.CreatedAt,
        StartedAt = j.StartedAt,
        CompletedAt = j.CompletedAt,
        UpdatedAt = j.UpdatedAt,
        PollUrl = $"/api/aihub/responses/{j.JobId}",
        Metadata = TryExtractIngestionMetadata(j.IngestionContext),
    };

    /// <summary>
    /// Lê apenas o sub-objeto <c>metadata</c> do <c>IngestionContext</c> JSONB
    /// pra ecoar no GET. Outros campos do contexto (url/headers/extractedContent)
    /// ficam fora — bytes do PDF extraído podem ter megabytes e não têm valor
    /// pro caller fazer polling. Jobs sem ingestão (POST /responses direto) caem
    /// em null e o campo é omitido na serialização (DefaultIgnoreCondition).
    /// </summary>
    private static Dictionary<string, string>? TryExtractIngestionMetadata(string? ingestionContext)
    {
        if (string.IsNullOrWhiteSpace(ingestionContext)) return null;
        try
        {
            using var doc = JsonDocument.Parse(ingestionContext);
            if (!doc.RootElement.TryGetProperty("metadata", out var metadataEl)
                || metadataEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            return metadataEl.Deserialize<Dictionary<string, string>>(IngestionJsonDefaults.Options);
        }
        catch (JsonException)
        {
            // IngestionContext corrompido — não deve quebrar o polling.
            return null;
        }
    }
}

/// <summary>Body do POST /api/aihub/responses.</summary>
public sealed class CreateStandaloneResponseRequest
{
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; init; }

    /// <summary>Input bruto repassado ao workflow (JSON string). Workflow define o shape.</summary>
    [JsonPropertyName("input")]
    public string? Input { get; init; }

    /// <summary>Metadata opcional propagado pra execução. Hoje só persiste em logs.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>Webhook opcional pra entrega de resultado quando job terminar.</summary>
    [JsonPropertyName("callback")]
    public ResponseCallbackTarget? Callback { get; init; }

    /// <summary>
    /// Idempotency key. Também aceita via header <c>Idempotency-Key</c>.
    /// Quando coincidir com um job existente do mesmo tenant/projeto, retorna
    /// esse mesmo job (sem duplicar). Conflito cross-tenant retorna 409.
    /// </summary>
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; init; }
}

/// <summary>Payload de resposta do GET /api/aihub/responses/{jobId}.</summary>
public sealed class StandaloneResponse
{
    [JsonPropertyName("jobId")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; init; }

    [JsonPropertyName("executionId")]
    public string? ExecutionId { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("step")]
    public string? Step { get; init; }

    [JsonPropertyName("attempt")]
    public int Attempt { get; init; }

    [JsonPropertyName("output")]
    public string? Output { get; init; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }

    [JsonPropertyName("pollUrl")]
    public string PollUrl { get; init; } = string.Empty;

    /// <summary>
    /// Metadata enviada no POST original — preservada no JSONB do job e ecoada
    /// aqui pra que o caller correlacione resultado ↔ contexto sem precisar
    /// armazenar mapping próprio. Omitida do payload quando o job não foi criado
    /// via <c>/ingestions</c> ou não recebeu metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>Item retornado pelo GET /api/aihub/responses/{jobId}/deliveries.</summary>
public sealed class DeliveryItemResponse
{
    [JsonPropertyName("deliveryId")] public string DeliveryId { get; init; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("lastResponseCode")] public int? LastResponseCode { get; init; }
    [JsonPropertyName("lastError")] public string? LastError { get; init; }
    [JsonPropertyName("deliveredAt")] public DateTime? DeliveredAt { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
}
