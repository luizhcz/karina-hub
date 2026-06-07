using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints de analytics de decisões de Router por projeto. Extrai
/// intent + confidence direto do payload de <c>workflow_event_audit</c>
/// (sem tabela nova, sem instrumentação extra no hot path).
/// </summary>
[ApiController]
[Route("api/aihub/analytics/router-decisions")]
[Produces("application/json")]
public sealed class RouterDecisionAnalyticsController : ControllerBase
{
    private readonly IRouterDecisionAnalyticsRepository _repo;
    private readonly IProjectContextAccessor _projectCtx;
    private readonly IProjectRepository _projectRepo;
    private readonly IUserContextAccessor _userAccessor;
    private readonly bool _gateEnabled;

    public RouterDecisionAnalyticsController(
        IRouterDecisionAnalyticsRepository repo,
        IProjectContextAccessor projectCtx,
        IProjectRepository projectRepo,
        IUserContextAccessor userAccessor,
        IOptions<AdminOptions> adminOptions)
    {
        _repo = repo;
        _projectCtx = projectCtx;
        _projectRepo = projectRepo;
        _userAccessor = userAccessor;
        _gateEnabled = adminOptions.Value.GateEnabled;
    }

    [HttpGet("summary")]
    [SwaggerOperation(Summary = "Resumo de decisões de Router no período (default: mês corrente). " +
                                "Inclui top intents com confiança p50/p95, decisões de baixa confiança e ambiguity rate.")]
    [ProducesResponseType(typeof(RouterDecisionOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var (gate, resolved) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        var (fromDt, toDt) = ResolveRange(from, to);
        var overview = await _repo.GetOverviewAsync(resolved!, fromDt, toDt, ct);
        return Ok(overview);
    }

    [HttpGet("timeseries")]
    [SwaggerOperation(Summary = "Série temporal de decisões por bucket (day|hour) com confiança média e contadores de baixa-confiança/ambiguidade.")]
    [ProducesResponseType(typeof(IReadOnlyList<RouterDecisionTimeseriesBucket>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeseries(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? groupBy,
        CancellationToken ct)
    {
        var (gate, resolved) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        var (fromDt, toDt) = ResolveRange(from, to);
        var buckets = await _repo.GetTimeseriesAsync(resolved!, fromDt, toDt, groupBy ?? "day", ct);
        return Ok(buckets);
    }

    private async Task<(IActionResult? Gate, string? ProjectId)> ResolveProjectGateAsync(
        string? requestedProjectId, CancellationToken ct)
    {
        var ctxProjectId = _projectCtx.Current?.ProjectId;
        var projectId = !string.IsNullOrWhiteSpace(requestedProjectId) ? requestedProjectId : ctxProjectId;

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

    private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var f = from ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = to ?? now;
        return (f, t);
    }
}
