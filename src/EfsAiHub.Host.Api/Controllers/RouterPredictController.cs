using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Host.Api.AgentSandbox;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint dedicado pra testar Router em isolamento. Router é classificador —
/// rodar end-to-end exige branches reais ou mockadas, o que polui o teste.
/// Aqui chamamos o LLM diretamente, devolvemos <c>intent</c>/<c>reasoning</c>
/// e nada é persistido (stateless).
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class RouterPredictController : ControllerBase
{
    private readonly AgentSandboxService _service;
    private readonly UserIdentityResolver _identityResolver;

    public RouterPredictController(
        AgentSandboxService service,
        UserIdentityResolver identityResolver)
    {
        _service = service;
        _identityResolver = identityResolver;
    }

    [HttpPost("api/aihub/agents/{agentId}/predict-intent")]
    [SwaggerOperation(
        Summary = "Roda o Router em modo classificador stateless",
        Description = "Não cria session nem workflow. Chama o LLM com o input " +
                      "e parseia o JSON de saída pra extrair intent/reasoning. " +
                      "Retorna 400 se agent não é Router ou está desabilitado.")]
    [ProducesResponseType(typeof(AgentSandboxService.RouterPredictResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Predict(
        string agentId,
        [FromBody] RouterPredictRequestBody body,
        CancellationToken ct)
    {
        var identity = _identityResolver.TryResolve(Request.Headers, out var error);
        if (identity is null) return BadRequest(new { error });

        try
        {
            var result = await _service.PredictRouterIntentAsync(
                agentId,
                new AgentSandboxService.RouterPredictRequest(body.Input, body.AgentVersionId),
                ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed record RouterPredictRequestBody(string Input, string? AgentVersionId);
