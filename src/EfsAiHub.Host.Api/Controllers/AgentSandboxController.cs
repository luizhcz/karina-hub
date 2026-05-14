using EfsAiHub.Core.Abstractions.AgentSandbox;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Host.Api.AgentSandbox;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints unificados pra Agent Sandbox — backend deriva o <c>Mode</c>
/// (chat ou standalone) pelo tipo do agent. Caller só passa <c>agentId</c>
/// (e opcionalmente <c>agentVersionId</c>) e roteia o usuário pra UI correta
/// via <c>response.mode</c>.
///
/// Rotas legacy <c>/chat-sandbox-sessions/*</c> em
/// <see cref="ChatSandboxController"/> continuam funcionando como shim
/// (delegam pro mesmo service); marcadas como obsoletas pra cleanup futuro.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class AgentSandboxController : ControllerBase
{
    private readonly AgentSandboxService _service;
    private readonly UserIdentityResolver _identityResolver;

    public AgentSandboxController(
        AgentSandboxService service,
        UserIdentityResolver identityResolver)
    {
        _service = service;
        _identityResolver = identityResolver;
    }

    [HttpPost("api/aihub/agents/{agentId}/sandbox-sessions")]
    [SwaggerOperation(
        Summary = "Cria uma session de sandbox isolada pra qualquer tipo de agent",
        Description = "Backend decide o Mode: Conversational → workflow Chat + conversation; " +
                      "Custom/Worker/ToolRunner → workflow Standalone single-shot; Router → 400 " +
                      "(use /predict-intent). Response carrega Mode pra frontend rotear UI.")]
    [ProducesResponseType(typeof(AgentSandboxSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSession(
        string agentId,
        [FromBody] AgentSandboxCreateRequest? body,
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

            var response = AgentSandboxSessionResponse.FromDomain(session);
            return CreatedAtAction(
                nameof(GetById),
                new { sessionId = response.SessionId },
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

    [HttpGet("api/aihub/agents/{agentId}/sandbox-sessions")]
    [SwaggerOperation(Summary = "Lista sandbox sessions de um agente, mais recentes primeiro")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentSandboxSessionResponse>), StatusCodes.Status200OK)]
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
        return Ok(sessions.Select(AgentSandboxSessionResponse.FromDomain));
    }

    [HttpGet("api/aihub/sandbox-sessions/{sessionId}")]
    [SwaggerOperation(Summary = "Retorna metadados de uma sandbox session específica")]
    [ProducesResponseType(typeof(AgentSandboxSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string sessionId, CancellationToken ct)
    {
        var session = await _service.GetByIdAsync(sessionId, ct);
        if (session is null) return NotFound();
        return Ok(AgentSandboxSessionResponse.FromDomain(session));
    }

    [HttpDelete("api/aihub/sandbox-sessions/{sessionId}")]
    [SwaggerOperation(Summary = "Encerra uma sandbox session (Status=Closed). Sessions Validated não podem ser fechadas.")]
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

    [HttpPost("api/aihub/sandbox-sessions/{sessionId}/validate")]
    [SwaggerOperation(
        Summary = "Marca a session como Validated e popula LastChatSandboxValidated* no agent",
        Description = "Admin-only. Em V1 só pra Mode=chat (sessions standalone retornam 400). " +
                      "Exige ≥1 turn prévio. Audita ChatSandboxValidated.")]
    [ProducesResponseType(typeof(AgentSandboxSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(
        string sessionId,
        [FromBody] AgentSandboxValidateRequest? body,
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
            return Ok(AgentSandboxSessionResponse.FromDomain(session));
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

public sealed record AgentSandboxCreateRequest(string? AgentVersionId);

public sealed record AgentSandboxValidateRequest(string? Notes);

public sealed record AgentSandboxSessionResponse(
    string SessionId,
    string AgentId,
    string AgentVersionId,
    string Mode,
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
    public static AgentSandboxSessionResponse FromDomain(AgentSandboxSession s) => new(
        s.SandboxSessionId,
        s.AgentId,
        s.AgentVersionId,
        s.Mode,
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
