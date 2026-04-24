using System.Text;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Host.Api.Models.Requests;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/conversations")]
[Produces("application/json")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationFacade _facade;
    private readonly IWorkflowEventBus _eventBus;
    private readonly IExecutionDetailReader _detailReader;
    private readonly UserIdentityResolver _identityResolver;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(
        IConversationFacade facade,
        IWorkflowEventBus eventBus,
        IExecutionDetailReader detailReader,
        UserIdentityResolver identityResolver,
        ILogger<ConversationsController> logger)
    {
        _facade = facade;
        _eventBus = eventBus;
        _detailReader = detailReader;
        _identityResolver = identityResolver;
        _logger = logger;
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria uma nova conversa de chat")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromBody] CreateConversationRequest request,
        CancellationToken ct)
    {
        var user = _identityResolver.TryResolve(Request.Headers, out var errorMsg);
        if (user is null) return BadRequest(errorMsg);

        var result = await _facade.CreateAsync(
            request.WorkflowId, user.UserId, user.UserType, request.Metadata, ct);

        if (result.Status != ConversationOperationStatus.Ok)
            return MapError(result.Status, result.ErrorMessage);

        var session = result.Value!;
        return CreatedAtAction(nameof(GetById), new { id = session.ConversationId }, new
        {
            conversationId = session.ConversationId,
            userId = session.UserId,
            userType = session.UserType,
            workflowId = session.WorkflowId,
            createdAt = session.CreatedAt
        });
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Retorna metadados de uma conversa")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var session = await _facade.GetAsync(id, ct);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpGet("{id}/messages")]
    [SwaggerOperation(Summary = "Lista histórico de mensagens da conversa (mais recentes primeiro)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMessages(
        string id,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var result = await _facade.ListMessagesAsync(id, limit, offset, ct);
        if (result.Status != ConversationOperationStatus.Ok)
            return MapError(result.Status, result.ErrorMessage);

        return Ok(result.Value!.Select(m => new
        {
            m.MessageId,
            m.Role,
            message = m.Content,
            output = m.StructuredOutput?.RootElement,
            m.CreatedAt,
            m.ExecutionId
        }));
    }

    [HttpGet("{id}/full")]
    [SwaggerOperation(Summary = "Dump completo da conversa: metadata, todas as mensagens e execuções (nodes/tools/events) referenciadas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFull(string id, CancellationToken ct)
    {
        var session = await _facade.GetAsync(id, ct);
        if (session is null) return NotFound();

        // Hard cap: 1000 mensagens mais recentes. Acima disso, paginação manual.
        var msgResult = await _facade.ListMessagesAsync(id, limit: 1000, offset: 0, ct);
        if (msgResult.Status != ConversationOperationStatus.Ok)
            return MapError(msgResult.Status, msgResult.ErrorMessage);

        var messages = msgResult.Value!;
        var executionIds = messages
            .Select(m => m.ExecutionId)
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct()
            .ToList();

        var details = await _detailReader.GetFullBatchAsync(executionIds!, ct);
        var executions = details.Select(detail => new
        {
            execution = Models.Responses.ExecutionDetailResponse.FromDomain(detail.Execution),
            nodes = detail.Nodes,
            tools = detail.Tools,
            events = detail.Events
        }).ToList<object>();

        return Ok(new
        {
            conversation = session,
            messages = messages.Select(m => new
            {
                m.MessageId,
                m.Role,
                message = m.Content,
                output = m.StructuredOutput?.RootElement,
                m.CreatedAt,
                m.ExecutionId
            }),
            executions
        });
    }

    [HttpPost("{id}/messages")]
    [SwaggerOperation(Summary = "Envia mensagens para a conversa (dispara workflow se a última não for 'robot')")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendMessages(
        string id,
        [FromBody] List<ChatMessageInputDto> inputs,
        CancellationToken ct)
    {
        var user = _identityResolver.TryResolve(Request.Headers, out var errorMsg);
        if (user is null) return BadRequest(errorMsg);

        if (inputs is null)
            return BadRequest("A lista de mensagens não pode ser vazia.");

        var result = await _facade.SendMessagesAsync(
            id, user.UserId,
            inputs.Select(i => new ChatMessageInput(i.Role, i.Message)).ToList(),
            ct);

        if (result.Status != ConversationOperationStatus.Ok)
            return MapError(result.Status, result.ErrorMessage);

        var sendResult = result.Value!;
        if (!string.IsNullOrEmpty(sendResult.TooEarlyReason))
            return Conflict(sendResult.TooEarlyReason);

        return Ok(new
        {
            executionId = sendResult.ExecutionId,
            hitlResolved = sendResult.HitlResolved,
            messageIds = sendResult.PersistedMessages?.Select(m => m.MessageId)
        });
    }

    [HttpGet("{id}/messages/stream")]
    [SwaggerOperation(Summary = "SSE: stream de eventos em tempo real para a conversa ativa")]
    public async Task StreamMessages(string id, CancellationToken ct)
    {
        var session = await _facade.GetAsync(id, ct);
        if (session is null)
        {
            Response.StatusCode = 404;
            return;
        }

        if (string.IsNullOrEmpty(session.ActiveExecutionId))
        {
            Response.StatusCode = 204;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        await foreach (var envelope in _eventBus.SubscribeAsync(session.ActiveExecutionId, ct))
        {
            var sseType = envelope.EventType switch
            {
                "hitl_required" => "waiting_for_input",
                "workflow_completed" => "message_complete",
                _ => envelope.EventType
            };

            var line = $"event: {sseType}\ndata: {envelope.Payload}\n\n";
            await Response.WriteAsync(line, Encoding.UTF8, ct);
            await Response.Body.FlushAsync(ct);

            if (envelope.EventType is "workflow_completed" or "error")
                break;
        }
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Deleta uma conversa e todas as suas mensagens")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await _facade.DeleteAsync(id, ct);
        return result.Status == ConversationOperationStatus.Ok
            ? NoContent()
            : MapError(result.Status, result.ErrorMessage);
    }

    [HttpGet("/api/admin/conversations")]
    [SwaggerOperation(Summary = "Admin: lista todas as conversas com filtros opcionais")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAdmin(
        [FromQuery] string? userId,
        [FromQuery] string? workflowId,
        [FromQuery] string? projectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var (items, total) = await _facade.ListAllAsync(userId, workflowId, projectId, from, to, page, pageSize, ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpDelete("{id}/context")]
    [SwaggerOperation(Summary = "Reseta o contexto da conversa (mensagens antigas ficam visíveis, mas não são enviadas ao próximo workflow)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearContext(string id, CancellationToken ct)
    {
        var result = await _facade.ClearContextAsync(id, ct);
        return result.Status == ConversationOperationStatus.Ok
            ? NoContent()
            : MapError(result.Status, result.ErrorMessage);
    }

    private IActionResult MapError(ConversationOperationStatus status, string? message) => status switch
    {
        ConversationOperationStatus.NotFound => NotFound(message),
        ConversationOperationStatus.BadRequest => BadRequest(message),
        ConversationOperationStatus.RateLimited => StatusCode(429, message),
        ConversationOperationStatus.Conflict => Conflict(message),
        _ => StatusCode(500, message)
    };
}
