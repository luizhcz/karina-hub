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
/// Endpoints de analytics de webhook delivery (<c>aihub.webhook_deliveries</c>)
/// por projeto. Mostra delivery rate, p95 latência, breakdown por status HTTP
/// e top hosts. Authorization idêntica ao <see cref="ToolAnalyticsController"/>.
/// </summary>
[ApiController]
[Route("api/aihub/analytics/webhooks")]
[Produces("application/json")]
public sealed class WebhookDeliveryAnalyticsController : ControllerBase
{
    private readonly IWebhookDeliveryAnalyticsRepository _repo;
    private readonly IProjectContextAccessor _projectCtx;
    private readonly IProjectRepository _projectRepo;
    private readonly IUserContextAccessor _userAccessor;
    private readonly bool _gateEnabled;

    public WebhookDeliveryAnalyticsController(
        IWebhookDeliveryAnalyticsRepository repo,
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
    [SwaggerOperation(Summary = "Resumo de entrega de webhooks no período (default: mês corrente). " +
                                "Inclui taxa de entrega, p95 latência, breakdown por classe HTTP (2xx/4xx/5xx/no-response) e top hosts.")]
    [ProducesResponseType(typeof(WebhookDeliveryOverview), StatusCodes.Status200OK)]
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
    [SwaggerOperation(Summary = "Série temporal de tentativas + entregas + falhas + p95 latência por bucket (day|hour).")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookDeliveryTimeseriesBucket>), StatusCodes.Status200OK)]
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
