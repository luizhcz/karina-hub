using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents.Capture;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Services;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Tela admin de inspeção de prompts LLM. Gate via <see cref="Middleware.AdminGateMiddleware"/>
/// (toda rota <c>/api/aihub/admin/*</c> bloqueada pra non-admin). Audit log:
/// só no <c>/curl</c> (export). Visualizações normais não poluem o
/// <c>admin_audit_log</c> — admins debugando produção fariam dezenas de
/// reads por sessão.
/// </summary>
[ApiController]
[Route("api/aihub/admin")]
[Produces("application/json")]
public sealed class AdminLlmInvocationsController : ControllerBase
{
    private readonly LlmCaptureConfigService _captureConfig;
    private readonly ILlmInvocationLogRepository _logRepo;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public AdminLlmInvocationsController(
        LlmCaptureConfigService captureConfig,
        ILlmInvocationLogRepository logRepo,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _captureConfig = captureConfig;
        _logRepo = logRepo;
        _audit = audit;
        _auditContext = auditContext;
    }

    // ─── Capture config ─────────────────────────────────────────────────────

    [HttpGet("llm-capture/config")]
    [SwaggerOperation(Summary = "Estado atual da captura LLM (ON/OFF + scope + TTL).")]
    public async Task<IActionResult> GetCaptureConfig(CancellationToken ct)
    {
        var cfg = await _captureConfig.GetCurrentAsync(ct);
        return Ok(new
        {
            enabled = cfg.Enabled,
            projectIds = cfg.ProjectIds,
            agentIds = cfg.AgentIds,
            workflowIds = cfg.WorkflowIds,
            expiresAt = cfg.ExpiresAt,
            enabledBy = cfg.EnabledBy,
            enabledAt = cfg.EnabledAt,
            updatedAt = cfg.UpdatedAt,
            secondsRemaining = cfg.ExpiresAt is { } x
                ? Math.Max(0, (int)(x - DateTime.UtcNow).TotalSeconds)
                : (int?)null,
        });
    }

    [HttpPut("llm-capture/config")]
    [SwaggerOperation(Summary = "Liga/desliga captura LLM. TTL via durationHours; null = sem auto-off.")]
    public async Task<IActionResult> SetCaptureConfig(
        [FromBody] CaptureConfigRequest request,
        CancellationToken ct)
    {
        if (request.Enabled && request.DurationHours is { } h && (h <= 0 || h > 168))
            return BadRequest(new { error = "durationHours deve ser >0 e <=168 (7 dias)." });

        var now = DateTime.UtcNow;
        var actorUserId = _auditContext.GetActorUserId();
        var next = new LlmCaptureConfig(
            Enabled: request.Enabled,
            ProjectIds: request.Enabled ? request.ProjectIds : null,
            AgentIds: request.Enabled ? request.AgentIds : null,
            WorkflowIds: request.Enabled ? request.WorkflowIds : null,
            ExpiresAt: request.Enabled && request.DurationHours is { } d
                ? now.AddHours(d) : null,
            EnabledBy: request.Enabled ? actorUserId : null,
            EnabledAt: request.Enabled ? now : null,
            UpdatedAt: now);

        var saved = await _captureConfig.UpdateAsync(next, ct);

        // Audita toggle pra ter trilha de "quem ligou captura quando".
        await _audit.RecordAsync(_auditContext.Build(
            saved.Enabled ? "llm_capture.enabled" : "llm_capture.disabled",
            "llm_capture_config",
            "singleton",
            payloadAfter: AdminAuditContext.Snapshot(new
            {
                enabled = saved.Enabled,
                projectIds = saved.ProjectIds,
                agentIds = saved.AgentIds,
                workflowIds = saved.WorkflowIds,
                expiresAt = saved.ExpiresAt,
            })), ct);

        return Ok(new
        {
            enabled = saved.Enabled,
            expiresAt = saved.ExpiresAt,
            enabledBy = saved.EnabledBy,
        });
    }

    // ─── LLM calls list/detail ──────────────────────────────────────────────

    [HttpGet("llm-calls")]
    [SwaggerOperation(Summary = "Lista paginada de chamadas LLM capturadas com filtros.")]
    public async Task<IActionResult> ListCalls(
        [FromQuery] string? agentId,
        [FromQuery] string? projectId,
        [FromQuery] string? intent,
        [FromQuery] string? status,
        [FromQuery] string? executionId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] double? minDurationMs,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        CancellationToken ct = default)
    {
        var q = new LlmInvocationLogQuery(
            AgentId: agentId, ProjectId: projectId, Intent: intent, Status: status,
            ExecutionId: executionId, From: from, To: to, MinDurationMs: minDurationMs,
            Page: Math.Max(1, page), Size: Math.Clamp(size, 1, 200));

        var rows = await _logRepo.ListAsync(q, ct);
        var total = await _logRepo.CountAsync(q, ct);

        return Ok(new
        {
            page = q.Page,
            size = q.Size,
            total,
            items = rows.Select(ToListDto).ToList(),
        });
    }

    [HttpGet("llm-calls/{id:long}")]
    [SwaggerOperation(Summary = "Detalhe completo de uma chamada LLM (request, response, composition, chatOptions).")]
    public async Task<IActionResult> GetCall(long id, CancellationToken ct)
    {
        var row = await _logRepo.GetByIdAsync(id, ct);
        if (row is null) return NotFound();
        return Ok(ToDetailDto(row));
    }

    [HttpGet("llm-calls/{id:long}/curl")]
    [SwaggerOperation(Summary = "Reconstrói comando cURL pra reproduzir a chamada em playground externo. Grava em admin_audit_log.")]
    public async Task<IActionResult> ExportCurl(long id, CancellationToken ct)
    {
        var row = await _logRepo.GetByIdAsync(id, ct);
        if (row is null) return NotFound();

        var curl = BuildCurlCommand(row);

        // Audit-on-export: gera entry pra que outro admin saiba quem visualizou
        // o conteúdo cru (pode conter PII do usuário do chat).
        await _audit.RecordAsync(_auditContext.Build(
            "llm_call.export",
            "llm_invocation_log",
            row.Id?.ToString() ?? id.ToString(),
            payloadAfter: AdminAuditContext.Snapshot(new
            {
                agentId = row.AgentId,
                executionId = row.ExecutionId,
                intent = row.Intent,
                createdAt = row.CreatedAt,
            })), ct);

        return Ok(new { command = curl });
    }

    [HttpGet("llm-calls/{id:long}/diff")]
    [SwaggerOperation(Summary = "Diff entre o turno atual e um anterior do mesmo agente (campo a campo).")]
    public async Task<IActionResult> Diff(long id, [FromQuery] long? vs, CancellationToken ct)
    {
        var current = await _logRepo.GetByIdAsync(id, ct);
        if (current is null) return NotFound(new { error = "Turno atual não encontrado." });

        LlmInvocationLogEntry? previous = null;
        if (vs.HasValue)
        {
            previous = await _logRepo.GetByIdAsync(vs.Value, ct);
        }
        else
        {
            var recent = await _logRepo.RecentForDiffAsync(
                current.AgentId, current.ExecutionId, current.CreatedAt, limit: 1, ct);
            previous = recent.FirstOrDefault();
        }

        if (previous is null)
            return Ok(new { current = ToDetailDto(current), previous = (object?)null, diff = Array.Empty<object>() });

        // Diff superficial: campos top-level estruturais. Frontend renderiza.
        var diff = new List<object>
        {
            Cmp("intent", previous.Intent, current.Intent),
            Cmp("model", previous.Model, current.Model),
            Cmp("status", previous.Status, current.Status),
            Cmp("inputTokens", previous.InputTokens, current.InputTokens),
            Cmp("outputTokens", previous.OutputTokens, current.OutputTokens),
            Cmp("durationMs", previous.DurationMs, current.DurationMs),
            Cmp("requestSizeBytes", previous.RequestSizeBytes, current.RequestSizeBytes),
            Cmp("responseSizeBytes", previous.ResponseSizeBytes, current.ResponseSizeBytes),
        };
        return Ok(new
        {
            current = ToDetailDto(current),
            previous = ToDetailDto(previous),
            diff = diff.Where(d => d is not null).ToList(),
        });
    }

    [HttpGet("llm-calls/{id:long}/recent-for-diff")]
    [SwaggerOperation(Summary = "Lista turnos anteriores do mesmo agente+execution disponíveis pra diff.")]
    public async Task<IActionResult> RecentForDiff(long id, [FromQuery] int limit = 10, CancellationToken ct = default)
    {
        var current = await _logRepo.GetByIdAsync(id, ct);
        if (current is null) return NotFound();
        var rows = await _logRepo.RecentForDiffAsync(
            current.AgentId, current.ExecutionId, current.CreatedAt,
            Math.Clamp(limit, 1, 50), ct);
        return Ok(rows.Select(ToListDto));
    }

    // ─── DTOs / helpers ─────────────────────────────────────────────────────

    private static object ToListDto(LlmInvocationLogEntry e) => new
    {
        id = e.Id,
        turnId = e.TurnId,
        attemptIndex = e.AttemptIndex,
        agentId = e.AgentId,
        projectId = e.ProjectId,
        executionId = e.ExecutionId,
        intent = e.Intent,
        provider = e.Provider,
        providerResolved = e.ProviderResolved,
        model = e.Model,
        status = e.Status,
        durationMs = e.DurationMs,
        inputTokens = e.InputTokens,
        outputTokens = e.OutputTokens,
        truncated = e.Truncated,
        createdAt = e.CreatedAt,
    };

    private static object ToDetailDto(LlmInvocationLogEntry e) => new
    {
        id = e.Id,
        turnId = e.TurnId,
        attemptIndex = e.AttemptIndex,
        executionId = e.ExecutionId,
        workflowId = e.WorkflowId,
        agentId = e.AgentId,
        agentVersionId = e.AgentVersionId,
        projectId = e.ProjectId,
        provider = e.Provider,
        providerResolved = e.ProviderResolved,
        model = e.Model,
        intent = e.Intent,
        status = e.Status,
        errorMessage = e.ErrorMessage,
        durationMs = e.DurationMs,
        inputTokens = e.InputTokens,
        outputTokens = e.OutputTokens,
        cachedTokens = e.CachedTokens,
        requestSizeBytes = e.RequestSizeBytes,
        responseSizeBytes = e.ResponseSizeBytes,
        truncated = e.Truncated,
        request = ParseJson(e.RequestPayload),
        response = ParseJson(e.ResponsePayload),
        composition = e.Composition,
        chatOptions = ParseJson(e.ChatOptionsSnapshot),
        createdAt = e.CreatedAt,
    };

    private static object? ParseJson(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static object? Cmp(string field, object? before, object? after)
    {
        if (Equals(before, after)) return null;
        return new { field, before, after };
    }

    private static string BuildCurlCommand(LlmInvocationLogEntry e)
    {
        // Reconstrução conservadora: monta um shape OpenAI-compatível.
        // Authorization fica como placeholder pra admin trocar pela própria key.
        // Não exporta o sanitizer-marker — colocamos um placeholder explícito.
        string endpoint = e.Provider switch
        {
            "AzureOpenAI" => "https://YOUR_AZURE_RESOURCE.openai.azure.com/openai/deployments/" + e.Model + "/chat/completions?api-version=2024-12-01-preview",
            "OpenAI" => "https://api.openai.com/v1/chat/completions",
            "AzureFoundry" => "https://YOUR_AZURE_RESOURCE.cognitiveservices.azure.com/openai/deployments/" + e.Model + "/chat/completions?api-version=2024-12-01-preview",
            _ => "https://api.openai.com/v1/chat/completions",
        };

        // Body simplificado a partir do request payload capturado.
        var body = ParseJson(e.RequestPayload);
        var bodyObj = new
        {
            model = e.Model,
            messages = body,
            temperature = 0.0,
        };
        var bodyJson = JsonSerializer.Serialize(bodyObj, new JsonSerializerOptions { WriteIndented = true });

        var sb = new StringBuilder();
        sb.AppendLine($"# Provider: {e.Provider} ({e.ProviderResolved})  Model: {e.Model}");
        sb.AppendLine($"# Captured at: {e.CreatedAt:O}");
        sb.AppendLine($"curl -X POST '{endpoint}' \\");
        sb.AppendLine("  -H 'Content-Type: application/json' \\");
        sb.AppendLine("  -H 'Authorization: Bearer $YOUR_API_KEY' \\");
        sb.Append("  -d '").Append(bodyJson.Replace("'", "'\\''")).Append("'");
        return sb.ToString();
    }
}

public sealed class CaptureConfigRequest
{
    public bool Enabled { get; init; }
    public IReadOnlyList<string>? ProjectIds { get; init; }
    public IReadOnlyList<string>? AgentIds { get; init; }
    public IReadOnlyList<string>? WorkflowIds { get; init; }
    /// <summary>1..168 (até 7 dias). null = sem auto-off (admin desliga manualmente).</summary>
    public int? DurationHours { get; init; }
}
