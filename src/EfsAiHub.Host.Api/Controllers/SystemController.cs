using System.Text.Json.Serialization;
using EfsAiHub.Platform.Runtime.Resilience;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/system")]
[Produces("application/json")]
public class SystemController : ControllerBase
{
    private readonly LlmCircuitBreaker _circuitBreaker;

    public SystemController(LlmCircuitBreaker circuitBreaker)
    {
        _circuitBreaker = circuitBreaker;
    }

    [HttpGet("health/circuit-breakers")]
    [SwaggerOperation(Summary = "Retorna o estado atual de todos os circuit breakers de LLM (snapshot point-in-time)")]
    [ProducesResponseType(typeof(CircuitBreakersResponse), StatusCodes.Status200OK)]
    public IActionResult GetCircuitBreakers()
    {
        var states = _circuitBreaker.GetAllStates();

        var result = states.Select(kv => new CircuitBreakerStateDto
        {
            ProviderKey         = kv.Key,
            Status              = kv.Value.Status,
            ConsecutiveFailures = kv.Value.ConsecutiveFailures,
            OpensAt             = kv.Value.OpensAt,
            HalfOpenDeadline    = kv.Value.HalfOpenDeadline,
            IsOperational       = kv.Value.Status == CircuitStatus.Closed,
        }).OrderBy(x => x.ProviderKey).ToList();

        return Ok(new CircuitBreakersResponse { CircuitBreakers = result });
    }

}

// ── Response DTOs ────────────────────────────────────────────────────────────

public class CircuitBreakersResponse
{
    public List<CircuitBreakerStateDto> CircuitBreakers { get; init; } = [];
}

public class CircuitBreakerStateDto
{
    public string ProviderKey { get; init; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CircuitStatus Status { get; init; }

    public int ConsecutiveFailures { get; init; }
    public DateTime? OpensAt { get; init; }
    public DateTime? HalfOpenDeadline { get; init; }
    public bool IsOperational { get; init; }
}

