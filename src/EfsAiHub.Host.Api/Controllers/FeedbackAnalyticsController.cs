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
/// Endpoints de analytics de feedback (<c>aihub.message_feedbacks</c>)
/// escopados por projeto. Mostra likes/dislikes, satisfação, e listagem
/// paginada com preview da mensagem avaliada (JOIN com chat_messages).
/// Authorization idêntica ao <see cref="ToolAnalyticsController"/>.
/// </summary>
[ApiController]
[Route("api/aihub/analytics/feedback")]
[Produces("application/json")]
public sealed class FeedbackAnalyticsController : ControllerBase
{
    private readonly IFeedbackAnalyticsRepository _repo;
    private readonly IProjectContextAccessor _projectCtx;
    private readonly IProjectRepository _projectRepo;
    private readonly IUserContextAccessor _userAccessor;
    private readonly bool _gateEnabled;

    public FeedbackAnalyticsController(
        IFeedbackAnalyticsRepository repo,
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
    [SwaggerOperation(Summary = "Resumo de feedback no período (default: mês corrente). " +
                                "Inclui likes/dislikes, satisfaction rate, distinct users + top mensagens com mais avaliações.")]
    [ProducesResponseType(typeof(FeedbackOverview), StatusCodes.Status200OK)]
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
    [SwaggerOperation(Summary = "Série temporal de feedback por bucket (day|hour) — total, positives, negatives.")]
    [ProducesResponseType(typeof(IReadOnlyList<FeedbackTimeseriesBucket>), StatusCodes.Status200OK)]
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

    [HttpGet("recent")]
    [SwaggerOperation(Summary = "Listagem paginada de feedbacks recentes com preview da mensagem avaliada. " +
                                "Filtros: ?sentiment=1 (likes) ou ?sentiment=-1 (dislikes); page/pageSize.")]
    [ProducesResponseType(typeof(FeedbackRecentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecent(
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? sentiment,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (gate, resolved) = await ResolveProjectGateAsync(projectId, ct);
        if (gate is not null) return gate;

        // Sentiment é binário ±1 (constraint do schema). Valores fora desse
        // domínio caem em null (sem filtro) — comportamento defensivo:
        // melhor ignorar param inválido que devolver 400 e quebrar UI.
        var safeSentiment = sentiment is 1 or -1 ? sentiment : null;
        var safePage = page is null or < 1 ? 1 : page.Value;
        var safePageSize = pageSize is null or < 1 ? 25 : Math.Min(200, pageSize.Value);

        var (fromDt, toDt) = ResolveRange(from, to);
        var result = await _repo.GetRecentAsync(resolved!, fromDt, toDt, safeSentiment, safePage, safePageSize, ct);
        return Ok(result);
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
