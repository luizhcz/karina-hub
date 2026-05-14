using System.Text.Json.Serialization;
using EfsAiHub.Platform.Runtime.Resilience;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/system")]
[Produces("application/json")]
public class SystemController : ControllerBase
{
    private readonly LlmCircuitBreaker _circuitBreaker;
    private readonly IConfiguration _config;

    public SystemController(LlmCircuitBreaker circuitBreaker, IConfiguration config)
    {
        _circuitBreaker = circuitBreaker;
        _config = config;
    }

    [HttpGet("info")]
    [SwaggerOperation(Summary = "Metadados públicos do backend — baseUrl + headers que clientes externos devem enviar (configurado em EfsAiHub:ConsumeHeaders no appsettings). UI de doc copia esses valores nos exemplos de implantação.")]
    [ProducesResponseType(typeof(SystemInfoResponse), StatusCodes.Status200OK)]
    public IActionResult GetInfo()
    {
        // Override explícito (prod atrás de ingress/proxy onde Request.Host pode
        // apontar pra rede interna). Quando ausente, deriva do request — útil
        // em dev/homolog onde a URL pública é a mesma da requisição.
        var configured = _config["EfsAiHub:PublicBaseUrl"];
        var baseUrl = string.IsNullOrWhiteSpace(configured)
            ? $"{Request.Scheme}://{Request.Host}"
            : configured.TrimEnd('/');

        // Headers que o consumidor externo precisa enviar (app_origin,
        // access_token, etc.). Config é fonte da verdade — alterar headers
        // exigidos não precisa de deploy do FE, só edit no appsettings.
        // Entradas com Key vazia são ignoradas (defensivo contra config malformada).
        var headers = _config.GetSection("EfsAiHub:ConsumeHeaders")
            .GetChildren()
            .Select(s => new ConsumeHeaderDto
            {
                Key = s["Key"] ?? string.Empty,
                Value = s["Value"] ?? string.Empty,
            })
            .Where(h => !string.IsNullOrWhiteSpace(h.Key))
            .ToList();

        return Ok(new SystemInfoResponse
        {
            PublicBaseUrl = baseUrl,
            ConsumeHeaders = headers,
        });
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

public sealed class SystemInfoResponse
{
    public required string PublicBaseUrl { get; init; }
    public List<ConsumeHeaderDto> ConsumeHeaders { get; init; } = [];
}

public sealed class ConsumeHeaderDto
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}

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

