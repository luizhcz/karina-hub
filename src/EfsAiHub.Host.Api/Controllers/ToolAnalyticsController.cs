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
/// Endpoints de uso de ferramentas escopados por projeto. Alimentam a tela
/// "Ferramentas" do dashboard analytics (chamada de ferramenta) — mostra
/// quais tools foram invocadas, com que frequência, taxa de falha e latência.
///
/// Authorization espelha o <see cref="ProjectAnalyticsController"/>: non-admin
/// só pode ler o próprio projeto (403 cross-project). SQL recebe ProjectId
/// parametrizado como defesa em profundidade.
///
/// Production-only: o repository faz INNER JOIN com <c>v_production_executions</c>
/// (filtra workflows sandbox de chat e standalone), então sandbox session
/// nunca aparece aqui.
/// </summary>
[ApiController]
[Route("api/aihub/analytics/tools")]
[Produces("application/json")]
public sealed class ToolAnalyticsController : ControllerBase
{
    private readonly IToolAnalyticsRepository _repo;
    private readonly IProjectContextAccessor _projectCtx;
    private readonly IProjectRepository _projectRepo;
    private readonly IUserContextAccessor _userAccessor;
    private readonly bool _gateEnabled;

    public ToolAnalyticsController(
        IToolAnalyticsRepository repo,
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
    [SwaggerOperation(Summary = "Resumo de uso de ferramentas no projeto (default: mês corrente). " +
                                "Retorna agregado total + linhas por tool ordenadas por chamadas DESC. " +
                                "Filtra execuções sandbox automaticamente (v_production_executions).")]
    [ProducesResponseType(typeof(ToolUsageOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var (gate, resolvedProjectId) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        var (fromDt, toDt) = ResolveRange(from, to);
        var overview = await _repo.GetToolUsageOverviewAsync(resolvedProjectId!, fromDt, toDt, ct);
        return Ok(overview);
    }

    [HttpGet("timeseries")]
    [SwaggerOperation(Summary = "Série temporal de chamadas + falhas + latência por bucket (day|hour). " +
                                "Default: últimos 30 dias agrupados por dia.")]
    [ProducesResponseType(typeof(IReadOnlyList<ToolTimeseriesBucket>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeseries(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? groupBy,
        CancellationToken ct)
    {
        var (gate, resolvedProjectId) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        var (fromDt, toDt) = ResolveRange(from, to);
        var buckets = await _repo.GetToolTimeseriesAsync(
            resolvedProjectId!, fromDt, toDt, groupBy ?? "day", ct);
        return Ok(buckets);
    }

    /// <summary>
    /// Resolve o ProjectId final + gate de autorização. ProjectId pode vir via
    /// query param (?projectId=) OU pelo <see cref="IProjectContextAccessor"/>
    /// (header x-project-id). Query param tem precedência pra admin que quer
    /// ver outro projeto sem trocar de sessão. 403 quando non-admin tenta
    /// projeto diferente do current; 404 quando o projeto não existe.
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

    /// <summary>Default: mês corrente até agora. Mesmo padrão do ProjectAnalyticsController.</summary>
    private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var f = from ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var t = to ?? now;
        return (f, t);
    }
}
