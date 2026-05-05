using System.Text.Json;
using EfsAiHub.Core.Abstractions.Events;
using EfsAiHub.Host.Api.Endpoints.Polling;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Gerencia sessões de conversa multi-turn com agentes.
///
/// Fluxo típico:
///   1. POST /api/aihub/agents/{agentId}/sessions          → cria sessão
///   2. POST /api/aihub/agents/{agentId}/sessions/{id}/run → envia mensagem, recebe resposta
///   3. (Repetir step 2 para múltiplos turns)
///   4. DELETE /api/aihub/agents/{agentId}/sessions/{id}   → encerra sessão
/// </summary>
[ApiController]
[Route("api/aihub/agents/{agentId}/sessions")]
[Produces("application/json")]
public class AgentSessionsController : ControllerBase
{
    private readonly AgentSessionService _sessionService;
    private readonly IEventBuffer _eventBuffer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentSessionsController> _logger;

    public AgentSessionsController(
        AgentSessionService sessionService,
        IEventBuffer eventBuffer,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentSessionsController> logger)
    {
        _sessionService = sessionService;
        _eventBuffer = eventBuffer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private static string SessionStreamKey(string agentId, string sessionId) =>
        $"agent-session:{agentId}:{sessionId}";

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

        var streamKey = SessionStreamKey(agentId, sessionId);
        // Tee de tokens pro IEventBuffer enquanto entrega via SSE — clientes do
        // /events podem pollar em paralelo. Fire-and-forget no append: falha de
        // buffer NUNCA interrompe o SSE (fonte primária).
        try
        {
            await foreach (var token in _sessionService.RunStreamingAsync(sessionId, request.Message, ct))
            {
                await Response.WriteAsync($"data: {token}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                _ = AppendBufferTokenAsync(streamKey, token);
            }

            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
            _ = AppendBufferDoneAsync(streamKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = AppendBufferErrorAsync(streamKey, ex.Message);
            throw;
        }
    }

    [HttpPost("{sessionId}/run-async")]
    [SwaggerOperation(
        Summary = "Polling fallback HTTP — dispara um turn em background e libera consumer pra pollar /events",
        Description = "Igual ao /stream mas sem long-lived connection: retorna 202 imediatamente; " +
                      "tokens são publicados no IEventBuffer e consumidos via GET /events?since=N.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunAsync(
        string agentId,
        string sessionId,
        [FromBody] SessionRunRequest request,
        CancellationToken ct)
    {
        var record = await _sessionService.GetAsync(sessionId, ct);
        if (record is null || !record.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var streamKey = SessionStreamKey(agentId, sessionId);

        // Roda em background via scope isolado — ct do request não amarra a execução
        // (HTTP fechou em 202; o turn continua até completar). Mesma estratégia do
        // WorkflowService.TriggerAsync.
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
            var buffer = scope.ServiceProvider.GetRequiredService<IEventBuffer>();
            try
            {
                await foreach (var token in inner.RunStreamingAsync(sessionId, request.Message, CancellationToken.None))
                {
                    await buffer.AppendAsync(streamKey,
                        new BufferEvent("token", JsonSerializer.Serialize(new { value = token }), DateTimeOffset.UtcNow),
                        CancellationToken.None);
                }
                await buffer.AppendAsync(streamKey,
                    new BufferEvent("done", "{}", DateTimeOffset.UtcNow),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AgentSession run-async error for stream {StreamKey}", streamKey);
                try
                {
                    await buffer.AppendAsync(streamKey,
                        new BufferEvent("error", JsonSerializer.Serialize(new { message = ex.Message }), DateTimeOffset.UtcNow),
                        CancellationToken.None);
                }
                catch { /* buffer pode estar em estado inconsistente; ignora */ }
            }
        }, CancellationToken.None);

        return Accepted(new { sessionId, streamKey });
    }

    [HttpGet("{sessionId}/events")]
    [SwaggerOperation(Summary = "Polling fallback HTTP — alternativa ao /stream pra clientes sem SSE")]
    [ProducesResponseType(typeof(EventPollingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Events(
        string agentId,
        string sessionId,
        [FromQuery] long? since,
        [FromQuery] int? limit,
        [FromQuery] int? waitMs,
        CancellationToken ct = default)
    {
        var (s, l, w, err) = EventPollingValidation.Parse(since, limit, waitMs);
        if (err is not null) return BadRequest(new { error = err });

        var record = await _sessionService.GetAsync(sessionId, ct);
        if (record is null || !record.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var streamKey = SessionStreamKey(agentId, sessionId);
        var page = await _eventBuffer.ReadSinceAsync(streamKey, s, l, w, ct);

        var items = page.Events.Select(e => new EventPollingItem(
            Seq: e.Seq,
            Type: e.Type,
            Payload: DbPollingHelper.ToJsonElement(e.PayloadJson),
            OccurredAt: e.OccurredAt)).ToList();

        return Ok(new EventPollingResponse(items, page.NextSince, page.Terminal));
    }

    private async Task AppendBufferTokenAsync(string streamKey, string token)
    {
        try
        {
            await _eventBuffer.AppendAsync(streamKey,
                new BufferEvent("token", JsonSerializer.Serialize(new { value = token }), DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Buffer append (token) failed for {StreamKey}", streamKey);
        }
    }

    private async Task AppendBufferDoneAsync(string streamKey)
    {
        try
        {
            await _eventBuffer.AppendAsync(streamKey,
                new BufferEvent("done", "{}", DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Buffer append (done) failed for {StreamKey}", streamKey);
        }
    }

    private async Task AppendBufferErrorAsync(string streamKey, string message)
    {
        try
        {
            await _eventBuffer.AppendAsync(streamKey,
                new BufferEvent("error", JsonSerializer.Serialize(new { message }), DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Buffer append (error) failed for {StreamKey}", streamKey);
        }
    }
}
