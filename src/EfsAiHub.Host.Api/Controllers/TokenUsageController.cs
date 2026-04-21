using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/token-usage")]
[Produces("application/json")]
public class TokenUsageController : ControllerBase
{
    private readonly ILlmTokenUsageRepository _repo;

    public TokenUsageController(ILlmTokenUsageRepository repo) => _repo = repo;

    [HttpGet("summary")]
    [SwaggerOperation(Summary = "Resumo global de uso de tokens por agente em um período")]
    public async Task<IActionResult> GetGlobalSummary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;
        var summary = await _repo.GetGlobalSummaryAsync(fromDate, toDate, ct);
        return Ok(summary);
    }

    [HttpGet("agents/{agentId}/summary")]
    [SwaggerOperation(Summary = "Resumo de uso de tokens de um agente específico")]
    public async Task<IActionResult> GetAgentSummary(
        string agentId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;
        var summary = await _repo.GetAgentSummaryAsync(agentId, fromDate, toDate, ct);
        return Ok(summary);
    }

    [HttpGet("agents/{agentId}/history")]
    [SwaggerOperation(Summary = "Histórico detalhado de chamadas LLM de um agente")]
    public async Task<IActionResult> GetAgentHistory(
        string agentId,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;
        var history = await _repo.GetByAgentIdAsync(agentId, limit, ct);
        return Ok(history);
    }

    [HttpGet("executions/{executionId}")]
    [SwaggerOperation(Summary = "Uso de tokens de uma execução específica")]
    public async Task<IActionResult> GetByExecution(string executionId, CancellationToken ct)
    {
        var usages = await _repo.GetByExecutionIdAsync(executionId, ct);
        return Ok(usages);
    }

    [HttpGet("throughput")]
    [SwaggerOperation(Summary = "Throughput por hora: execuções, tokens e chamadas LLM")]
    public async Task<IActionResult> GetThroughput(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var fromDate = from ?? DateTime.UtcNow.AddHours(-24);
        var toDate = to ?? DateTime.UtcNow;
        var result = await _repo.GetThroughputAsync(fromDate, toDate, ct);
        return Ok(result);
    }

    [HttpGet("workflows/summary")]
    [SwaggerOperation(Summary = "Resumo de tokens e custo por workflow em um período")]
    public async Task<IActionResult> GetWorkflowsSummary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;
        var result = await _repo.GetAllWorkflowsSummaryAsync(fromDate, toDate, ct);
        return Ok(result);
    }

    [HttpGet("projects/summary")]
    [SwaggerOperation(Summary = "Resumo de tokens e custo por projeto em um período")]
    public async Task<IActionResult> GetProjectsSummary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;
        var result = await _repo.GetAllProjectsSummaryAsync(fromDate, toDate, ct);
        return Ok(result);
    }
}
