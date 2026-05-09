using EfsAiHub.Core.Agents;
using EfsAiHub.Host.Api.Models.Responses;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/agents/{agentId}/operational-memory")]
[Produces("application/json")]
public sealed class OperationalMemoryController : ControllerBase
{
    private const string ScopeConversation = "conversation";
    private const string ScopeSession = "session";

    private readonly IOperationalMemoryRepository _repo;

    public OperationalMemoryController(IOperationalMemoryRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todas as entries de memória operacional do agente no projeto atual. Admin-only — non-admin recebe 403 via AdminGate.")]
    [ProducesResponseType(typeof(IReadOnlyList<OperationalMemoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(string agentId, CancellationToken ct)
    {
        var records = await _repo.ListAsync(agentId, ct);
        return Ok(records.Select(OperationalMemoryResponse.FromDomain));
    }

    [HttpGet("{scopeId}")]
    [SwaggerOperation(Summary = "Busca a memória de um escopo específico. ?scopeType=conversation|session (default conversation). 404 se inexistente.")]
    [ProducesResponseType(typeof(OperationalMemoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string agentId,
        string scopeId,
        [FromQuery] string? scopeType,
        CancellationToken ct)
    {
        if (!TryNormalizeScopeType(scopeType, out var resolvedScope, out var error))
            return BadRequest(new { error });

        var record = await _repo.GetAsync(agentId, resolvedScope, scopeId, ct);
        return record is null ? NotFound() : Ok(OperationalMemoryResponse.FromDomain(record));
    }

    [HttpDelete("{scopeId}")]
    [SwaggerOperation(Summary = "Reseta a memória de um escopo. ?scopeType=conversation|session (default conversation). Próximo turno do agente recria do zero.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string agentId,
        string scopeId,
        [FromQuery] string? scopeType,
        CancellationToken ct)
    {
        if (!TryNormalizeScopeType(scopeType, out var resolvedScope, out var error))
            return BadRequest(new { error });

        var removed = await _repo.DeleteAsync(agentId, resolvedScope, scopeId, ct);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Aceita "conversation" | "session" (case-insensitive), default
    /// "conversation". Qualquer outro valor → 400 com mensagem clara.
    /// </summary>
    private static bool TryNormalizeScopeType(string? input, out string normalized, out string? error)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            normalized = ScopeConversation;
            error = null;
            return true;
        }

        var trimmed = input.Trim().ToLowerInvariant();
        if (trimmed == ScopeConversation || trimmed == ScopeSession)
        {
            normalized = trimmed;
            error = null;
            return true;
        }

        normalized = string.Empty;
        error = $"scopeType inválido '{input}'. Valores aceitos: '{ScopeConversation}' | '{ScopeSession}'.";
        return false;
    }
}
