using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints de uso/custo escopados por projeto. Alimentam o dashboard que
/// PMs/POs do banco abrem pra ver consumo MTD, série temporal, breakdown de
/// agentes e status de orçamento. Audiência: usuário do próprio projeto +
/// admins (que veem qualquer um).
///
/// Authorization: helper <see cref="EnsureProjectAccessAsync"/> bate
/// 403 quando non-admin tenta ler outro projeto. Mesmo padrão do
/// AgentsController/WorkflowsController. SQL raw recebe ProjectId
/// parametrizado como defesa em profundidade.
/// </summary>
[ApiController]
[Route("api/aihub/analytics/projects")]
[Produces("application/json")]
public sealed class ProjectAnalyticsController : ControllerBase
{
    private readonly IProjectAnalyticsRepository _repo;
    private readonly IProjectContextAccessor _projectCtx;
    private readonly IProjectRepository _projectRepo;
    private readonly UserIdentityResolver _identityResolver;
    private readonly HashSet<string> _adminAccountIds;

    public ProjectAnalyticsController(
        IProjectAnalyticsRepository repo,
        IProjectContextAccessor projectCtx,
        IProjectRepository projectRepo,
        UserIdentityResolver identityResolver,
        IOptions<AdminOptions> adminOptions)
    {
        _repo = repo;
        _projectCtx = projectCtx;
        _projectRepo = projectRepo;
        _identityResolver = identityResolver;
        _adminAccountIds = new HashSet<string>(adminOptions.Value.AccountIds, StringComparer.Ordinal);
    }

    [HttpGet("{projectId}/overview")]
    [SwaggerOperation(Summary = "Resumo de uso/custo do projeto no período (default: mês até agora). " +
                                "Inclui top 3 agentes por custo. Usado no card-resumo do dashboard.")]
    [ProducesResponseType(typeof(ProjectOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOverview(
        string projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var gate = await EnsureProjectAccessAsync(projectId, ct);
        if (gate is not null) return gate;

        var (fromDt, toDt) = ResolveRange(from, to);
        var overview = await _repo.GetProjectOverviewAsync(projectId, fromDt, toDt, ct);
        return Ok(overview);
    }

    [HttpGet("{projectId}/timeseries")]
    [SwaggerOperation(Summary = "Série temporal de custo + execuções por bucket (day|hour). " +
                                "Default: últimos 30 dias agrupados por dia.")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectTimeseriesBucket>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeseries(
        string projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? groupBy,
        [FromQuery] string? excludeAgentIds,
        CancellationToken ct)
    {
        var gate = await EnsureProjectAccessAsync(projectId, ct);
        if (gate is not null) return gate;

        var (fromDt, toDt) = ResolveRange(from, to);
        // Aceita CSV ou múltiplos query params (?excludeAgentIds=a&excludeAgentIds=b
        // funciona via binder do ASP.NET, mas também aceita "a,b" pra simplicidade
        // do cliente). Trim defensivo.
        var excludeIds = string.IsNullOrWhiteSpace(excludeAgentIds)
            ? null
            : excludeAgentIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

        var buckets = await _repo.GetProjectTimeseriesAsync(
            projectId, fromDt, toDt, groupBy ?? "day", excludeIds, ct);
        return Ok(buckets);
    }

    [HttpGet("{projectId}/agents")]
    [SwaggerOperation(Summary = "Breakdown por agente: calls, tokens, custo, p95 latência, error rate. " +
                                "Ordenado por custo DESC. top default = 20.")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectAgentBreakdown>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgents(
        string projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? top,
        CancellationToken ct)
    {
        var gate = await EnsureProjectAccessAsync(projectId, ct);
        if (gate is not null) return gate;

        var (fromDt, toDt) = ResolveRange(from, to);
        var clampedTop = Math.Clamp(top ?? 20, 1, 100);
        var rows = await _repo.GetProjectAgentBreakdownAsync(projectId, fromDt, toDt, clampedTop, ct);
        return Ok(rows);
    }

    [HttpGet("{projectId}/budget")]
    [SwaggerOperation(Summary = "Status do orçamento diário do projeto. " +
                                "ProjectBudgetGuard é warning-only — `exceeded:true` indica alerta, mas plataforma não bloqueia execução.")]
    [ProducesResponseType(typeof(ProjectBudgetStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBudget(string projectId, CancellationToken ct)
    {
        var gate = await EnsureProjectAccessAsync(projectId, ct);
        if (gate is not null) return gate;

        // EnsureProjectAccessAsync já validou existência, mas precisamos do
        // settings pra calcular pct/exceeded. Reload barato (cache no repo).
        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        var max = project?.Settings;
        var status = await _repo.GetProjectBudgetStatusAsync(
            projectId,
            max?.MaxTokensPerDay,
            max?.MaxCostUsdPerDay,
            ct);
        return Ok(status);
    }

    /// <summary>
    /// Bate 403 quando non-admin tenta ler projeto diferente do current. 404
    /// quando o projeto não existe (HasQueryFilter já isola por tenant).
    /// Admins (account no AdminOptions.AccountIds) podem ler qualquer um —
    /// mesmo padrão do AdminGateMiddleware.
    /// </summary>
    private async Task<IActionResult?> EnsureProjectAccessAsync(string projectId, CancellationToken ct)
    {
        var isAdmin = ResolveIsAdmin();
        if (!isAdmin)
        {
            var current = _projectCtx.Current?.ProjectId;
            if (!string.Equals(current, projectId, StringComparison.Ordinal))
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Você só pode consultar analytics do seu próprio projeto."
                });
        }

        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        if (project is null) return NotFound();
        return null;
    }

    /// <summary>
    /// Resolve admin do request via UserIdentityResolver + AdminOptions.
    /// Mesmo critério do AdminGateMiddleware — não duplicamos lógica de gate
    /// global aqui, apenas a identificação binária admin/não-admin.
    /// </summary>
    private bool ResolveIsAdmin()
    {
        if (_adminAccountIds.Count == 0) return true; // gate desabilitado (dev)
        var identity = _identityResolver.TryResolve(Request.Headers, out _);
        return identity is not null && _adminAccountIds.Contains(identity.UserId);
    }

    /// <summary>Default: mês corrente até agora. Aceita override via query.</summary>
    private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var f = from ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = to ?? now;
        return (f, t);
    }
}
