using System.Globalization;
using System.Text;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Host.Api.Models.Requests.Evaluation;
using EfsAiHub.Host.Api.Models.Responses.Evaluation;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Evaluation;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints para criar e consultar <see cref="EvaluationRun"/>. Validações
/// (TestSetVersion não-Deprecated, EvaluatorConfigVersion válida, ExpectedToolCalls
/// vs tools do AgentVersion, estimativa de custo &lt; budget cap) ficam em
/// <see cref="IEvaluationService"/>.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class AgentEvaluationsController : ControllerBase
{
    private readonly IEvaluationService _evaluationService;
    private readonly IEvaluationRunRepository _runRepo;
    private readonly IEvaluationResultRepository _resultRepo;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly ILogger<AgentEvaluationsController> _logger;

    public AgentEvaluationsController(
        IEvaluationService evaluationService,
        IEvaluationRunRepository runRepo,
        IEvaluationResultRepository resultRepo,
        IProjectContextAccessor projectAccessor,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        ILogger<AgentEvaluationsController> logger)
    {
        _evaluationService = evaluationService;
        _runRepo = runRepo;
        _resultRepo = resultRepo;
        _projectAccessor = projectAccessor;
        _audit = audit;
        _auditContext = auditContext;
        _logger = logger;
    }

    [HttpPost("api/agents/{agentId}/evaluations/runs")]
    [SwaggerOperation(Summary = "Enfileira uma eval run manual contra a config indicada")]
    [ProducesResponseType(typeof(EnqueueEvaluationRunResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnqueueRun(
        string agentId, [FromBody] CreateEvaluationRunRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _evaluationService.EnqueueManualAsync(new EnqueueRunRequest(
                ProjectId: _projectAccessor.Current.ProjectId,
                AgentDefinitionId: agentId,
                TestSetVersionId: request.TestSetVersionId,
                EvaluatorConfigVersionId: request.EvaluatorConfigVersionId,
                AgentVersionId: request.AgentVersionId,
                TriggeredBy: ResolveActorUserId()), ct);

            await _audit.RecordAsync(_auditContext.Build(
                "Trigger",
                "EvaluationRun",
                result.RunId ?? "(none)",
                payloadAfter: AdminAuditContext.Snapshot(result)), ct);

            var response = new EnqueueEvaluationRunResponse(
                RunId: result.RunId,
                Status: result.Status?.ToString(),
                Skipped: result.Skipped,
                SkipReason: result.SkipReason,
                DeduplicatedFromExisting: result.DeduplicatedFromExisting);

            return AcceptedAtAction(nameof(GetRun), new { runId = result.RunId }, response);
        }
        catch (EvaluationValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("api/agents/{agentId}/evaluations/runs")]
    [SwaggerOperation(Summary = "Lista eval runs do agente (paginado, filtro por trigger_source)")]
    [ProducesResponseType(typeof(IReadOnlyList<EvaluationRunResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRunsByAgent(
        string agentId,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        [FromQuery] string? triggerSource = null,
        CancellationToken ct = default)
    {
        EvaluationTriggerSource? filter = null;
        if (!string.IsNullOrEmpty(triggerSource)
            && Enum.TryParse<EvaluationTriggerSource>(triggerSource, ignoreCase: true, out var ts))
            filter = ts;

        var runs = await _runRepo.ListByAgentDefinitionAsync(agentId, skip, take, filter, ct);
        var responses = new List<EvaluationRunResponse>(runs.Count);
        foreach (var run in runs)
        {
            var progress = await _resultRepo.GetProgressAsync(run.RunId, ct);
            var usage = await _resultRepo.GetUsageAsync(run.RunId, ct);
            responses.Add(EvaluationRunResponse.FromDomain(run, progress, usage));
        }
        return Ok(responses);
    }

    [HttpGet("api/evaluations/runs/{runId}", Name = "GetEvaluationRun")]
    [SwaggerOperation(Summary = "Detalhe de uma eval run + summary do progress")]
    [ProducesResponseType(typeof(EvaluationRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRun(string runId, CancellationToken ct)
    {
        var run = await _runRepo.GetByIdAsync(runId, ct);
        if (run is null) return NotFound();

        var progress = await _resultRepo.GetProgressAsync(runId, ct);
        var usage = await _resultRepo.GetUsageAsync(runId, ct);
        return Ok(EvaluationRunResponse.FromDomain(run, progress, usage));
    }

    [HttpGet("api/evaluations/runs/{runId}/results")]
    [SwaggerOperation(Summary = "Lista resultados de uma run (filter passed=, evaluator=)")]
    [ProducesResponseType(typeof(IReadOnlyList<EvaluationResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListResults(
        string runId,
        [FromQuery] bool? passed = null,
        [FromQuery] string? evaluator = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
    {
        var run = await _runRepo.GetByIdAsync(runId, ct);
        if (run is null) return NotFound();

        var results = await _resultRepo.ListByRunAsync(runId, passed, evaluator, skip, take, ct);
        return Ok(results.Select(EvaluationResultResponse.FromDomain));
    }

    [HttpPost("api/evaluations/runs/{runId}/cancel")]
    [SwaggerOperation(Summary = "Cancel idempotente. NOTIFY runner ativo (≤1s) + CAS Pending|Running → Cancelled.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(string runId, CancellationToken ct)
    {
        var run = await _runRepo.GetByIdAsync(runId, ct);
        if (run is null) return NotFound();

        var cancelled = await _evaluationService.CancelRunAsync(runId, ResolveActorUserId(), ct);

        if (cancelled)
        {
            await _audit.RecordAsync(_auditContext.Build(
                "Cancel",
                "EvaluationRun",
                runId,
                payloadAfter: AdminAuditContext.Snapshot(new { Cancelled = true })), ct);
        }

        // 204 mesmo quando idempotente: cancelar run já em estado terminal = no-op.
        return NoContent();
    }

    [HttpGet("api/evaluations/runs/{runId}/stream")]
    [SwaggerOperation(Summary = "SSE de progresso da run — emite deltas de progress até run virar terminal")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task StreamRun(string runId, [FromQuery] string? projectId, CancellationToken ct)
    {
        // EventSource do browser não envia headers customizados; projectId via
        // query param substitui `x-efs-project-id` quando middleware caiu no
        // Default sentinel.
        if (!string.IsNullOrEmpty(projectId) && !_projectAccessor.Current.IsExplicit)
        {
            _projectAccessor.Current = new EfsAiHub.Core.Abstractions.Identity.ProjectContext(projectId);
        }

        var run = await _runRepo.GetByIdAsync(runId, ct);
        if (run is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        // Desativa buffering em proxies (NGINX/Cloudflare) — sem isso eventos
        // ficam batched.
        Response.Headers.Append("X-Accel-Buffering", "no");

        var lastSnapshotJson = string.Empty;
        var keepAliveCount = 0;
        while (!ct.IsCancellationRequested)
        {
            var current = await _runRepo.GetByIdAsync(runId, ct);
            if (current is null)
            {
                await WriteSseEventAsync("error", "{\"error\":\"run_disappeared\"}", ct);
                break;
            }
            var progress = await _resultRepo.GetProgressAsync(runId, ct);
            var payload = SerializeProgressPayload(current, progress);
            if (payload != lastSnapshotJson)
            {
                await WriteSseEventAsync("progress", payload, ct);
                lastSnapshotJson = payload;
            }

            // Estado terminal — fecha o stream.
            if (current.Status is EvaluationRunStatus.Completed
                              or EvaluationRunStatus.Failed
                              or EvaluationRunStatus.Cancelled)
            {
                await WriteSseEventAsync("done", payload, ct);
                break;
            }

            // Keep-alive a cada 15s pra evitar timeout de proxy ocioso.
            keepAliveCount++;
            if (keepAliveCount % 15 == 0)
            {
                await Response.WriteAsync(": keep-alive\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static string SerializeProgressPayload(EvaluationRun run, EvaluationRunProgress? p)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            status = run.Status.ToString(),
            casesTotal = run.CasesTotal,
            casesCompleted = p?.CasesCompleted ?? 0,
            casesPassed = p?.CasesPassed ?? 0,
            casesFailed = p?.CasesFailed ?? 0,
            avgScore = p?.AvgScore,
            totalCostUsd = p?.TotalCostUsd ?? 0m,
            totalTokens = p?.TotalTokens ?? 0L,
            lastError = run.LastError,
            startedAt = run.StartedAt,
            completedAt = run.CompletedAt
        });
    }

    private async Task WriteSseEventAsync(string eventName, string dataJson, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {eventName}\ndata: {dataJson}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    [HttpGet("api/evaluations/runs/{runId}/export")]
    [SwaggerOperation(Summary = "Export de results em CSV ou JSON")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        string runId,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        var run = await _runRepo.GetByIdAsync(runId, ct);
        if (run is null) return NotFound();

        var results = await _resultRepo.ListByRunAsync(runId, ct: ct);
        var responses = results.Select(EvaluationResultResponse.FromDomain).ToList();

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(responses);
        }

        // CSV default.
        var sb = new StringBuilder();
        sb.AppendLine("ResultId,CaseId,EvaluatorName,BindingIndex,RepetitionIndex,Score,Passed,Reason,JudgeModel,LatencyMs,CostUsd,InputTokens,OutputTokens,CreatedAt");
        foreach (var r in responses)
        {
            sb.Append(CsvEscape(r.ResultId)).Append(',');
            sb.Append(CsvEscape(r.CaseId)).Append(',');
            sb.Append(CsvEscape(r.EvaluatorName)).Append(',');
            sb.Append(r.BindingIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RepetitionIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.Score?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.Append(r.Passed).Append(',');
            sb.Append(CsvEscape(r.Reason ?? string.Empty)).Append(',');
            sb.Append(CsvEscape(r.JudgeModel ?? string.Empty)).Append(',');
            sb.Append(r.LatencyMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.Append(r.CostUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.Append(r.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.Append(r.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.AppendLine(r.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"evaluation-run-{runId}.csv");
    }

    [HttpGet("api/evaluations/runs/compare")]
    [SwaggerOperation(Summary = "Compara 2 runs (cross-version) — retorna diff lado-a-lado e flag de regressão")]
    [ProducesResponseType(typeof(EvaluationRunCompareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Compare(
        [FromQuery] string runA,
        [FromQuery] string runB,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(runA) || string.IsNullOrEmpty(runB))
            return BadRequest(new { error = "Parâmetros runA e runB obrigatórios." });

        var rA = await _runRepo.GetByIdAsync(runA, ct);
        var rB = await _runRepo.GetByIdAsync(runB, ct);
        if (rA is null || rB is null) return NotFound();

        var pA = await _resultRepo.GetProgressAsync(runA, ct);
        var pB = await _resultRepo.GetProgressAsync(runB, ct);
        var resultsA = await _resultRepo.ListByRunAsync(runA, ct: ct);
        var resultsB = await _resultRepo.ListByRunAsync(runB, ct: ct);

        // Pass rate = avaliações pass / total de avaliações (CasesPassed/Failed
        // contam results individuais, não cases distintos).
        var totalA = (pA?.CasesPassed ?? 0) + (pA?.CasesFailed ?? 0);
        var totalB = (pB?.CasesPassed ?? 0) + (pB?.CasesFailed ?? 0);
        decimal? passRateA = totalA > 0 ? (decimal)pA!.CasesPassed / totalA : null;
        decimal? passRateB = totalB > 0 ? (decimal)pB!.CasesPassed / totalB : null;
        var passRateDelta = passRateA.HasValue && passRateB.HasValue ? passRateB - passRateA : null;
        var casesFailedDelta = (pB?.CasesFailed ?? 0) - (pA?.CasesFailed ?? 0);

        // Threshold ADR 0015: passRateDelta < -0.05 AND CasesFailedDelta >= 2.
        var regression = passRateDelta < -0.05m && casesFailedDelta >= 2;

        // Diff por caso — agrupa por CaseId (pode ter múltiplos evaluators × repetições).
        var byCaseA = resultsA.GroupBy(r => r.CaseId).ToDictionary(g => g.Key, Aggregate);
        var byCaseB = resultsB.GroupBy(r => r.CaseId).ToDictionary(g => g.Key, Aggregate);
        var allCaseIds = byCaseA.Keys.Union(byCaseB.Keys).ToList();
        var diffs = allCaseIds.Select(caseId =>
        {
            var a = byCaseA.GetValueOrDefault(caseId);
            var b = byCaseB.GetValueOrDefault(caseId);
            return new CaseDiff(caseId,
                a is null ? null : a.Passed,
                b is null ? null : b.Passed,
                a?.Score, b?.Score,
                a?.Reason, b?.Reason);
        }).ToList();

        return Ok(new EvaluationRunCompareResponse(
            RunIdA: runA,
            RunIdB: runB,
            PassRateA: passRateA,
            PassRateB: passRateB,
            PassRateDelta: passRateDelta,
            CasesFailedA: pA?.CasesFailed ?? 0,
            CasesFailedB: pB?.CasesFailed ?? 0,
            CasesFailedDelta: casesFailedDelta,
            RegressionDetected: regression,
            CaseDiffs: diffs));
    }

    private sealed record CaseAggregate(bool Passed, decimal? Score, string? Reason);

    private static CaseAggregate Aggregate(IEnumerable<EvaluationResult> group)
    {
        var list = group.ToList();
        var passed = list.All(r => r.Passed);
        var scores = list.Where(r => r.Score.HasValue).Select(r => r.Score!.Value).ToList();
        decimal? avgScore = scores.Count == 0 ? null : scores.Average();
        var firstReason = list.Select(r => r.Reason).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        return new CaseAggregate(passed, avgScore, firstReason);
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private string? ResolveActorUserId()
    {
        var headers = HttpContext?.Request?.Headers;
        if (headers is null) return null;
        var resolver = HttpContext!.RequestServices.GetService<UserIdentityResolver>();
        return resolver?.TryResolve(headers, out _)?.UserId;
    }
}
