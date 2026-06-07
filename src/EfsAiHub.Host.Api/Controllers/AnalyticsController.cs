using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Analytics agregados de execução de workflows. Sempre project-scoped — o
/// repository foi migrado pra <c>v_production_executions</c> (filtra sandbox)
/// e exige ProjectId na query SQL. Authorization espelha o
/// <see cref="ProjectAnalyticsController"/>: non-admin só pode ler o próprio
/// projeto.
/// </summary>
[ApiController]
[Route("api/aihub/analytics")]
[Produces("application/json")]
public class AnalyticsController : ControllerBase
{
    private readonly IExecutionAnalyticsRepository _analytics;
    private readonly IProjectContextAccessor _projectCtx;
    private readonly IProjectRepository _projectRepo;
    private readonly IUserContextAccessor _userAccessor;
    private readonly bool _gateEnabled;

    public AnalyticsController(
        IExecutionAnalyticsRepository analytics,
        IProjectContextAccessor projectCtx,
        IProjectRepository projectRepo,
        IUserContextAccessor userAccessor,
        IOptions<AdminOptions> adminOptions)
    {
        _analytics = analytics;
        _projectCtx = projectCtx;
        _projectRepo = projectRepo;
        _userAccessor = userAccessor;
        _gateEnabled = adminOptions.Value.GateEnabled;
    }

    [HttpGet("executions/summary")]
    [SwaggerOperation(Summary = "Resumo agregado de execuções com métricas de performance (success rate, P50/P95). " +
                                "Project-scoped via ?projectId= ou header x-project-id; filtra sandbox via v_production_executions.")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? workflowId,
        CancellationToken ct)
    {
        var (gate, resolved) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;
        var summary = await _analytics.GetSummaryAsync(resolved!, fromDate, toDate, workflowId, ct);
        return Ok(summary);
    }

    [HttpGet("executions/timeseries")]
    [SwaggerOperation(Summary = "Série temporal de execuções agrupadas por hora ou dia. " +
                                "Project-scoped; ?workflowId= permite drill-down opcional.")]
    public async Task<IActionResult> GetTimeseries(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? workflowId,
        [FromQuery] string groupBy = "hour",
        CancellationToken ct = default)
    {
        var (gate, resolved) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        var fromDate = from ?? DateTime.UtcNow.AddDays(-1);
        var toDate = to ?? DateTime.UtcNow;
        var buckets = await _analytics.GetTimeseriesAsync(resolved!, fromDate, toDate, workflowId, groupBy, ct);
        return Ok(new { buckets });
    }

    [HttpGet("executions/failure-breakdown")]
    [SwaggerOperation(Summary = "Breakdown de falhas por ErrorCategory no período — espelha a tag error.category da métrica workflows.failed")]
    public async Task<IActionResult> GetFailureBreakdown(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? workflowId,
        CancellationToken ct = default)
    {
        var (gate, resolved) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;
        var breakdown = await _analytics.GetFailureBreakdownAsync(resolved!, fromDate, toDate, workflowId, ct);
        return Ok(new { breakdown });
    }

    /// <summary>
    /// Mesma semântica do <see cref="ToolAnalyticsController"/>: ProjectId pode
    /// vir via ?projectId= ou pelo header x-project-id (IProjectContextAccessor).
    /// Query param tem precedência pra admin que quer ver outro projeto sem
    /// trocar de sessão. 403 quando non-admin tenta projeto diferente do current;
    /// 404 quando o projeto não existe; 400 quando nenhum projectId foi resolvido.
    /// </summary>
    private async Task<(IActionResult? Gate, string? ProjectId)> ResolveProjectGateAsync(
        string? requestedProjectId,
        CancellationToken ct)
    {
        var ctxProjectId = _projectCtx.Current?.ProjectId;
        var projectId = !string.IsNullOrWhiteSpace(requestedProjectId)
            ? requestedProjectId
            : ctxProjectId;

        if (string.IsNullOrWhiteSpace(projectId))
            return (BadRequest(new { error = "projectId obrigatório (via query ou header x-project-id)." }), null);

        var isAdmin = ResolveIsAdmin();
        if (!isAdmin)
        {
            if (!string.Equals(ctxProjectId, projectId, StringComparison.Ordinal))
                return (StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Você só pode consultar analytics do seu próprio projeto."
                }), null);
        }

        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        if (project is null) return (NotFound(), null);
        return (null, projectId);
    }

    private bool ResolveIsAdmin()
    {
        if (!_gateEnabled) return true;
        return _userAccessor.Current?.IsAdmin == true;
    }
}
