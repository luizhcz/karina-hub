using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/interactions")]
[Produces("application/json")]
public class InteractionsController : ControllerBase
{
    private readonly IHumanInteractionService _hitlService;
    private readonly UserIdentityResolver _identityResolver;

    public InteractionsController(
        IHumanInteractionService hitlService,
        UserIdentityResolver identityResolver)
    {
        _hitlService = hitlService;
        _identityResolver = identityResolver;
    }

    [HttpGet("pending")]
    [SwaggerOperation(Summary = "Lista todas as interações HITL pendentes de resposta")]
    public IActionResult GetPending()
        => Ok(_hitlService.GetPending());

    [HttpGet("{interactionId}")]
    [SwaggerOperation(Summary = "Busca uma interação pelo ID")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetById(string interactionId)
    {
        var interaction = _hitlService.GetById(interactionId);
        if (interaction is null) return NotFound();
        return Ok(interaction);
    }

    [HttpPost("{interactionId}/resolve")]
    [SwaggerOperation(Summary = "Resolve uma interação HITL pendente com a resposta do humano")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resolve(
        string interactionId, [FromBody] ResolveInteractionRequest request, CancellationToken ct)
    {
        // ResolvedBy capturado via x-efs-account / x-efs-user-profile-id. Headers são exigidos
        // pelo TenantMiddleware na maior parte das rotas, então aqui 400 só acontece em tooling
        // que bypassa middleware padrão (ex: cron/worker chamando direto).
        var identity = _identityResolver.TryResolve(Request.Headers, out var headerError);
        if (identity is null)
            return BadRequest(new { error = headerError ?? "Identificação do resolvedor ausente." });

        var resolved = await _hitlService.ResolveAsync(
            interactionId,
            request.Resolution,
            resolvedBy: identity.UserId,
            approved: request.Approved,
            ct: ct);
        if (!resolved) return NotFound(new { message = $"Interação '{interactionId}' não encontrada ou já resolvida." });
        return Ok(new { message = "Interação resolvida.", interactionId });
    }

    [HttpGet("by-execution/{executionId}")]
    [SwaggerOperation(Summary = "Lista interações HITL de uma execução específica")]
    public IActionResult GetByExecution(string executionId)
        => Ok(_hitlService.GetByExecutionId(executionId));
}
