using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Produces("application/json")]
public class AnalyticsController : ControllerBase
{
    private readonly IExecutionAnalyticsRepository _analytics;

    public AnalyticsController(IExecutionAnalyticsRepository analytics)
        => _analytics = analytics;

    [HttpGet("executions/summary")]
    [SwaggerOperation(Summary = "Resumo agregado de execuções com métricas de performance (success rate, P50/P95)")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? workflowId,
        CancellationToken ct)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;
        var summary = await _analytics.GetSummaryAsync(fromDate, toDate, workflowId, ct);
        return Ok(summary);
    }

    [HttpGet("executions/timeseries")]
    [SwaggerOperation(Summary = "Série temporal de execuções agrupadas por hora ou dia")]
    public async Task<IActionResult> GetTimeseries(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? workflowId,
        [FromQuery] string groupBy = "hour",
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-1);
        var toDate = to ?? DateTime.UtcNow;
        var buckets = await _analytics.GetTimeseriesAsync(fromDate, toDate, workflowId, groupBy, ct);
        return Ok(new { buckets });
    }
}
