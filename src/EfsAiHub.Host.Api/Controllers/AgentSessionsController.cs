using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Gerencia sessões de conversa multi-turn com agentes.
///
/// Fluxo típico:
///   1. POST /api/agents/{agentId}/sessions          → cria sessão
///   2. POST /api/agents/{agentId}/sessions/{id}/run → envia mensagem, recebe resposta
///   3. (Repetir step 2 para múltiplos turns)
///   4. DELETE /api/agents/{agentId}/sessions/{id}   → encerra sessão
/// </summary>
[ApiController]
[Route("api/agents/{agentId}/sessions")]
[Produces("application/json")]
public class AgentSessionsController : ControllerBase
{
    private readonly AgentSessionService _sessionService;

    public AgentSessionsController(AgentSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    // ── CRUD de sessões ──────────────────────────────────────────────────────

    [HttpPost]
    [SwaggerOperation(
        Summary = "Cria uma sessão de conversa multi-turn com um agente",
        Description = "A sessão mantém o histórico de mensagens entre turns. " +
                      "Use o sessionId retornado em todas as chamadas subsequentes.")]
    [ProducesResponseType(typeof(AgentSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(string agentId, CancellationToken ct)
    {
        var record = await _sessionService.CreateSessionAsync(agentId, ct: ct);
        return CreatedAtAction(
            nameof(GetById),
            new { agentId, sessionId = record.SessionId },
            AgentSessionResponse.FromDomain(record));
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todas as sessões ativas de um agente")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentSessionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(string agentId, CancellationToken ct)
    {
        var sessions = await _sessionService.ListByAgentAsync(agentId, ct);
        return Ok(sessions.Select(AgentSessionResponse.FromDomain));
    }

    [HttpGet("{sessionId}")]
    [SwaggerOperation(Summary = "Retorna metadados de uma sessão (não inclui histórico de mensagens)")]
    [ProducesResponseType(typeof(AgentSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string agentId, string sessionId, CancellationToken ct)
    {
        var record = await _sessionService.GetAsync(sessionId, ct);
        if (record is null || !record.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        return Ok(AgentSessionResponse.FromDomain(record));
    }

    [HttpDelete("{sessionId}")]
    [SwaggerOperation(Summary = "Encerra e remove uma sessão de conversa")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string agentId, string sessionId, CancellationToken ct)
    {
        var record = await _sessionService.GetAsync(sessionId, ct);
        if (record is null || !record.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        await _sessionService.DeleteAsync(sessionId, ct);
        return NoContent();
    }

    // ── Execução de turns ────────────────────────────────────────────────────

    [HttpPost("{sessionId}/run")]
    [SwaggerOperation(
        Summary = "Envia uma mensagem ao agente e recebe a resposta completa",
        Description = "Execução não-streaming. O histórico da conversa é mantido automaticamente na sessão.")]
    [ProducesResponseType(typeof(SessionRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Run(
        string agentId,
        string sessionId,
        [FromBody] SessionRunRequest request,
        CancellationToken ct)
    {
        var record = await _sessionService.GetAsync(sessionId, ct);
        if (record is null || !record.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var (response, updatedRecord) = await _sessionService.RunAsync(sessionId, request.Message, ct);

        return Ok(new SessionRunResponse
        {
            SessionId = sessionId,
            Response = response,
            TurnCount = updatedRecord.TurnCount
        });
    }

    /// <summary>
    /// Envia uma mensagem ao agente com resposta em Server-Sent Events (SSE).
    /// Conectar com Accept: text/event-stream.
    /// </summary>
    [HttpPost("{sessionId}/stream")]
    [SwaggerOperation(
        Summary = "Envia uma mensagem ao agente com resposta em streaming (SSE)",
        Description = "Retorna tokens incrementais via Server-Sent Events. " +
                      "Conectar com Content-Type: application/json e aceitar text/event-stream.")]
    public async Task Stream(
        string agentId,
        string sessionId,
        [FromBody] SessionRunRequest request,
        CancellationToken ct)
    {
        var record = await _sessionService.GetAsync(sessionId, ct);
        if (record is null || !record.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        await foreach (var token in _sessionService.RunStreamingAsync(sessionId, request.Message, ct))
        {
            await Response.WriteAsync($"data: {token}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
