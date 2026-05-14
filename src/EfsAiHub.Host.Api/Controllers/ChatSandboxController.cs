using EfsAiHub.Core.Abstractions.AgentSandbox;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Host.Api.AgentSandbox;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ChatSandboxController : ControllerBase
{
    private readonly AgentSandboxService _service;
    private readonly UserIdentityResolver _identityResolver;

    public ChatSandboxController(
        AgentSandboxService service,
        UserIdentityResolver identityResolver)
    {
        _service = service;
        _identityResolver = identityResolver;
    }

    [HttpPost("api/aihub/agents/{agentId}/chat-sandbox-sessions")]
    [SwaggerOperation(
        Summary = "Cria uma session de teste isolado de agente Conversational em chat AG-UI",
        Description = "Backend cria workflow Chat efêmero (com pin exato da versão do agente) + " +
                      "conversation. Retorna ids pro frontend dirigir o chat via /api/aihub/chat/ag-ui/stream.")]
    [ProducesResponseType(typeof(ChatSandboxSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSession(
        string agentId,
        [FromBody] ChatSandboxCreateRequest? body,
        CancellationToken ct)
    {
        var identity = _identityResolver.TryResolve(Request.Headers, out var error);
        if (identity is null) return BadRequest(new { error });

        var caller = new UserContext(identity.UserId, identity.UserType);
        try
        {
            var session = await _service.CreateSessionAsync(
                agentId,
                caller,
                new AgentSandboxService.CreateSessionRequest(body?.AgentVersionId),
                ct);

            var response = ChatSandboxSessionResponse.FromDomain(session);
            return CreatedAtAction(
                nameof(GetById),
                new { sessionId = response.ChatSandboxSessionId },
                response);
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

    [HttpGet("api/aihub/agents/{agentId}/chat-sandbox-sessions")]
    [SwaggerOperation(Summary = "Lista sessions de um agente, mais recentes primeiro")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatSandboxSessionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByAgent(
        string agentId,
        [FromQuery] string? status,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        AgentSandboxSessionStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AgentSandboxSessionStatus>(status, ignoreCase: true, out var parsed))
                return BadRequest(new { error = $"Status '{status}' inválido." });
            statusFilter = parsed;
        }

        var sessions = await _service.ListByAgentAsync(agentId, statusFilter, limit, ct);
        return Ok(sessions.Select(ChatSandboxSessionResponse.FromDomain));
    }

    [HttpGet("api/aihub/chat-sandbox-sessions/{sessionId}")]
    [SwaggerOperation(Summary = "Retorna metadados de uma session específica")]
    [ProducesResponseType(typeof(ChatSandboxSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string sessionId, CancellationToken ct)
    {
        var session = await _service.GetByIdAsync(sessionId, ct);
        if (session is null) return NotFound();
        return Ok(ChatSandboxSessionResponse.FromDomain(session));
    }

    [HttpDelete("api/aihub/chat-sandbox-sessions/{sessionId}")]
    [SwaggerOperation(Summary = "Encerra uma session (Status=Closed). Sessions Validated não podem ser fechadas.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Close(string sessionId, CancellationToken ct)
    {
        try
        {
            await _service.CloseSessionAsync(sessionId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("api/aihub/chat-sandbox-sessions/{sessionId}/validate")]
    [SwaggerOperation(
        Summary = "Marca a session como Validated e popula LastChatSandboxValidated* no agent",
        Description = "Admin-only. Exige ≥1 turn de teste prévio. Estado vira terminal — " +
                      "session não pode mais ser fechada nem reaberta. Audita ChatSandboxValidated.")]
    [ProducesResponseType(typeof(ChatSandboxSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(
        string sessionId,
        [FromBody] ChatSandboxValidateRequest? body,
        CancellationToken ct)
    {
        var identity = _identityResolver.TryResolve(Request.Headers, out var error);
        if (identity is null) return BadRequest(new { error });

        var caller = new UserContext(identity.UserId, identity.UserType);
        try
        {
            var session = await _service.ValidateAsync(
                sessionId,
                caller,
                new AgentSandboxService.ValidateSessionRequest(body?.Notes),
                ct);
            return Ok(ChatSandboxSessionResponse.FromDomain(session));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed record ChatSandboxCreateRequest(string? AgentVersionId);

public sealed record ChatSandboxValidateRequest(string? Notes);

/// <summary>
/// DTO de resposta da Chat Sandbox API legada. Field <c>ChatSandboxSessionId</c>
/// preservado pra estabilidade do contrato JSON (clientes existentes leem essa
/// chave); internamente o domínio chama de <c>SandboxSessionId</c>.
/// </summary>
public sealed record ChatSandboxSessionResponse(
    string ChatSandboxSessionId,
    string AgentId,
    string AgentVersionId,
    string WorkflowId,
    string? ConversationId,
    string ProjectId,
    string CreatedByUserId,
    DateTime CreatedAt,
    DateTime? LastMessageAt,
    DateTime ExpiresAt,
    string Status,
    DateTime? ValidatedAt,
    string? ValidatedByUserId,
    string? ValidationNotes)
{
    public static ChatSandboxSessionResponse FromDomain(AgentSandboxSession s) => new(
        s.SandboxSessionId,
        s.AgentId,
        s.AgentVersionId,
        s.WorkflowId,
        s.ConversationId,
        s.ProjectId,
        s.CreatedByUserId,
        s.CreatedAt,
        s.LastMessageAt,
        s.ExpiresAt,
        s.Status.ToString(),
        s.ValidatedAt,
        s.ValidatedByUserId,
        s.ValidationNotes);
}
