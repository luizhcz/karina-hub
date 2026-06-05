using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint da feature de ingestão: recebe URL, valida no servidor (SSRF,
/// magic bytes, MaxSize), encadeia Document Intelligence quando é PDF (TXT/MD
/// vão direto) e dispara workflow standalone. Cliente faz polling do estado
/// via <c>GET /api/aihub/responses/{jobId}</c> — o caminho de polling é o
/// mesmo da fila standalone (GET /api/aihub/responses/{jobId}).
///
/// Feature flag <c>IngestionApi:Enabled</c> guarda o endpoint — 503 quando
/// desligado. <c>StandalonePools:Enabled</c> também precisa estar on pro
/// dispatcher consumir o job que este controller enfileira.
/// </summary>
[ApiController]
[Route("api/aihub/ingestions")]
public sealed class IngestionsController : ControllerBase
{
    private static readonly Regex IdempotencyKeyPattern =
        new("^[A-Za-z0-9_:.-]{1,128}$", RegexOptions.Compiled);

    // Charset RFC 9110 field-value (subset prático): HT + printáveis ASCII.
    // Rejeita CR/LF (injeção de headers extras) e bytes de controle.
    private static readonly Regex HeaderValueCharsetPattern =
        new(@"^[\x09\x20-\x7E]*$", RegexOptions.Compiled);

    // Headers do download que o cliente NÃO pode override — pra não vazar
    // identidade do pod (Host), confundir o downloader (Content-Length /
    // Transfer-Encoding) ou romper keep-alive (Connection).
    private static readonly HashSet<string> ReservedHeaderNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Content-Length", "Transfer-Encoding",
            "Connection", "Keep-Alive", "Upgrade",
        };

    private const int MaxMetadataEntries = 64;
    private const int MaxMetadataBytes = 16 * 1024;
    private const int MaxCustomHeaders = 16;
    private const int MaxCustomHeaderBytes = 8 * 1024;

    private static readonly JsonSerializerOptions JsonOpts =
        EfsAiHub.Platform.Runtime.Ingestion.IngestionJsonDefaults.Options;

    private readonly IBackgroundResponseRepository _jobs;
    private readonly IngestionApiOptions _ingestionOptions;
    private readonly StandalonePoolsOptions _poolOptions;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<IngestionsController> _logger;

    public IngestionsController(
        IBackgroundResponseRepository jobs,
        IOptions<IngestionApiOptions> ingestionOptions,
        IOptions<StandalonePoolsOptions> poolOptions,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        ILogger<IngestionsController> logger)
    {
        _jobs = jobs;
        _ingestionOptions = ingestionOptions.Value;
        _poolOptions = poolOptions.Value;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Enfileira ingestão de URL → arquivo (PDF/TXT/MD) → Document Intelligence (PDF) → workflow standalone. Retorna 202 com jobId pra polling em GET /responses/{jobId}.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Enqueue([FromBody] CreateIngestionRequest request, CancellationToken ct)
    {
        if (!_ingestionOptions.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Pool de ingestão desabilitado (IngestionApi.Enabled=false)." });
        if (!_poolOptions.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Pool standalone desabilitado (StandalonePools.Enabled=false). Dispatcher não consumiria a fila." });

        if (request is null) return BadRequest(new { error = "Body obrigatório." });
        if (string.IsNullOrWhiteSpace(request.WorkflowId))
            return BadRequest(new { error = "workflowId obrigatório." });
        if (request.Source is null || string.IsNullOrWhiteSpace(request.Source.Url))
            return BadRequest(new { error = "source.url obrigatório." });

        if (!Uri.TryCreate(request.Source.Url, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https"))
        {
            return BadRequest(new { error = "source.url deve ser uma URL absoluta http/https." });
        }

        // UserInfo (`http://user:pass@host`) é vetor clássico de bypass — em
        // alguns parsers a authority resolve no host, em outros no userinfo.
        // Rejeita preventivamente.
        if (!string.IsNullOrEmpty(url.UserInfo))
            return BadRequest(new { error = "source.url não pode conter user:password (userinfo)." });

        Dictionary<string, string>? normalizedHeaders;
        try
        {
            normalizedHeaders = NormalizeHeaders(request.Source.Headers);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // Cap em metadata pra não inflar IngestionContext indefinidamente.
        if (request.Metadata is { Count: > 0 })
        {
            if (request.Metadata.Count > MaxMetadataEntries)
                return BadRequest(new { error = $"metadata excede {MaxMetadataEntries} chaves." });

            var totalBytes = request.Metadata.Sum(kv =>
                System.Text.Encoding.UTF8.GetByteCount(kv.Key ?? string.Empty)
                + System.Text.Encoding.UTF8.GetByteCount(kv.Value ?? string.Empty));
            if (totalBytes > MaxMetadataBytes)
                return BadRequest(new { error = $"metadata excede {MaxMetadataBytes} bytes agregados." });
        }

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
            if (existing is not null)
                return Conflict(new { error = "Idempotency-Key já em uso por outro tenant." });
        }

        var ingestionState = new IngestionStateDto
        {
            Url = url.ToString(),
            Headers = normalizedHeaders,
            Metadata = request.Metadata,
        };

        var job = new BackgroundResponseJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            WorkflowId = request.WorkflowId,
            AgentId = string.Empty,
            // Input vazio — IngestionContext carrega URL/metadata; o handler
            // de ingestão monta o envelope final que vai pro workflow.
            Input = string.Empty,
            CallbackTarget = request.Callback,
            IdempotencyKey = idempotencyKey,
            Status = BackgroundResponseStatus.Queued,
            IngestionContext = JsonSerializer.Serialize(ingestionState, JsonOpts),
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
            if (idempotencyKey is not null)
            {
                var existing = await _jobs.GetByIdempotencyKeyAsync(idempotencyKey, ct);
                if (existing is not null && BelongsToCurrentScope(existing))
                    return Accepted(BuildLocation(existing.JobId), ToResponse(existing));
            }
            return Conflict(new { error = "Conflito ao gravar o job." });
        }

        _logger.LogInformation(
            "[Ingestions] Job {JobId} enfileirado workflow={Wf} url={Url} tenant={Tenant} project={Project}.",
            job.JobId, job.WorkflowId, url, job.TenantId, job.ProjectId);

        MetricsRegistry.StandaloneJobsEnqueued.Add(1,
            new KeyValuePair<string, object?>("project_id", job.ProjectId),
            new KeyValuePair<string, object?>("source", "ingestions"));

        return Accepted(BuildLocation(job.JobId), ToResponse(job));
    }

    private string BuildLocation(string jobId) =>
        Url.Action("Get", "StandaloneResponses", new { jobId }) ?? $"/api/aihub/responses/{jobId}";

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

    private static Dictionary<string, string>? NormalizeHeaders(Dictionary<string, string>? raw)
    {
        if (raw is null || raw.Count == 0) return null;
        if (raw.Count > MaxCustomHeaders)
            throw new InvalidOperationException(
                $"source.headers excede {MaxCustomHeaders} chaves.");

        var copy = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;

        foreach (var (k, v) in raw)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            var name = k.Trim();
            var value = v ?? string.Empty;

            if (ReservedHeaderNames.Contains(name))
                throw new InvalidOperationException(
                    $"Header '{name}' é reservado e não pode ser sobrescrito pelo caller.");

            // CR/LF e bytes de controle: vetor de header injection.
            if (!HeaderValueCharsetPattern.IsMatch(value))
                throw new InvalidOperationException(
                    $"Header '{name}' contém caracteres não permitidos (CRLF ou byte de controle).");

            totalBytes += System.Text.Encoding.UTF8.GetByteCount(name)
                + System.Text.Encoding.UTF8.GetByteCount(value);
            if (totalBytes > MaxCustomHeaderBytes)
                throw new InvalidOperationException(
                    $"source.headers excede {MaxCustomHeaderBytes} bytes agregados.");

            copy[name] = value;
        }
        return copy.Count == 0 ? null : copy;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505") return true;
        return false;
    }

    private static IngestionAcceptedResponse ToResponse(BackgroundResponseJob j) => new()
    {
        JobId = j.JobId,
        WorkflowId = j.WorkflowId,
        Status = j.Status.ToString(),
        Step = j.Step,
        CreatedAt = j.CreatedAt,
        UpdatedAt = j.UpdatedAt,
        PollUrl = $"/api/aihub/responses/{j.JobId}",
    };
}

/// <summary>Body do POST /api/aihub/ingestions.</summary>
public sealed class CreateIngestionRequest
{
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; init; }

    [JsonPropertyName("source")]
    public IngestionSource? Source { get; init; }

    /// <summary>Metadata opcional propagada pro envelope do workflow.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>Webhook opcional pra entrega de resultado quando job terminar.</summary>
    [JsonPropertyName("callback")]
    public ResponseCallbackTarget? Callback { get; init; }

    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; init; }
}

public sealed class IngestionSource
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "url";

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Headers HTTP repassados no download (Authorization etc.).</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
}

/// <summary>Subset do que persistimos em IngestionContext na criação. Handler completa com detect/extract.</summary>
internal sealed class IngestionStateDto
{
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("headers")] public Dictionary<string, string>? Headers { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
}

public sealed class IngestionAcceptedResponse
{
    [JsonPropertyName("jobId")] public string JobId { get; init; } = string.Empty;
    [JsonPropertyName("workflowId")] public string? WorkflowId { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("step")] public string? Step { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
    [JsonPropertyName("pollUrl")] public string PollUrl { get; init; } = string.Empty;
}
