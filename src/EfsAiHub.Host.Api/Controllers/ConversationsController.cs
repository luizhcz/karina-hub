using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Host.Api.Endpoints.Polling;
using EfsAiHub.Host.Api.Models.Requests;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/conversations")]
[Produces("application/json")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationFacade _facade;
    private readonly IWorkflowEventBus _eventBus;
    private readonly IWorkflowEventRepository _eventRepo;
    private readonly IWorkflowService _workflowService;
    private readonly IExecutionDetailReader _detailReader;
    private readonly UserIdentityResolver _identityResolver;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(
        IConversationFacade facade,
        IWorkflowEventBus eventBus,
        IWorkflowEventRepository eventRepo,
        IWorkflowService workflowService,
        IExecutionDetailReader detailReader,
        UserIdentityResolver identityResolver,
        ILogger<ConversationsController> logger)
    {
        _facade = facade;
        _eventBus = eventBus;
        _eventRepo = eventRepo;
        _workflowService = workflowService;
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
            output = UnwrapStructuredOutput(m.StructuredOutput),
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
                output = UnwrapStructuredOutput(m.StructuredOutput),
                m.CreatedAt,
                m.ExecutionId
            }),
            executions
        });
    }

    // Admin: `output` expõe o payload estruturado do agente. O StructuredOutput
    // persistido é o envelope canônico inteiro (pra alimentar historyText no
    // histórico); desembrulha pro sub-`output` quando canônico, mantendo o
    // contrato (output = dados do agente, não os meta-campos do envelope).
    private static JsonElement? UnwrapStructuredOutput(JsonDocument? structured)
    {
        if (structured is null) return null;
        var root = structured.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return root;

        var isCanonical = root.TryGetProperty("output_type", out _)
            || root.TryGetProperty("output_status", out _);
        if (!isCanonical) return root;

        return root.TryGetProperty("output", out var output) ? output : null;
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

        // Header opcional pra pinar a execução em uma WorkflowVersion específica
        // (canary/A/B). Empty/whitespace tratado como ausente.
        var rawVersion = Request.Headers["x-version"].FirstOrDefault();
        var workflowVersionId = string.IsNullOrWhiteSpace(rawVersion) ? null : rawVersion;

        var result = await _facade.SendMessagesAsync(
            id, user.UserId,
            inputs.Select(i => new ChatMessageInput(i.Role, i.Message)).ToList(),
            ct,
            workflowVersionId);

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

    [HttpGet("{id}/messages/events")]
    [SwaggerOperation(Summary = "Polling fallback HTTP — alternativa ao /messages/stream pra clientes sem SSE")]
    [ProducesResponseType(typeof(EventPollingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EventsMessages(
        string id,
        [FromQuery] long? since,
        [FromQuery] int? limit,
        [FromQuery] int? waitMs,
        CancellationToken ct = default)
    {
        var (s, l, w, err) = EventPollingValidation.Parse(since, limit, waitMs);
        if (err is not null) return BadRequest(new { error = err });

        var session = await _facade.GetAsync(id, ct);
        if (session is null) return NotFound();

        // Sem execução ativa = não há eventos ainda. Responde shape válido pra cliente
        // continuar pollando (terminal=false) até a próxima mensagem disparar workflow.
        if (string.IsNullOrEmpty(session.ActiveExecutionId))
        {
            return Ok(new EventPollingResponse(Array.Empty<EventPollingItem>(), s, false));
        }

        var executionId = session.ActiveExecutionId;
        var response = await DbPollingHelper.PollAsync(
            readSince: async (cursor, max, innerCt) =>
            {
                var events = await _eventRepo.GetSinceAsync(executionId, cursor, max, innerCt);
                return events.Select(e => new EventPollingItem(
                    Seq: e.SequenceId,
                    Type: MapConversationEventType(e.EventType),
                    Payload: DbPollingHelper.ToJsonElement(e.Payload),
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc), TimeSpan.Zero))).ToList();
            },
            isTerminalAsync: async (_) =>
            {
                var current = await _workflowService.GetExecutionAsync(executionId, ct);
                return current is null || ExecutionsController.IsTerminalStatus(current.Status);
            },
            since: s,
            limit: l,
            waitFor: w,
            ct: ct);

        return Ok(response);
    }

    private static string MapConversationEventType(string raw) => raw switch
    {
        "hitl_required" => "waiting_for_input",
        "workflow_completed" => "message_complete",
        _ => raw
    };

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

    [HttpGet("/api/aihub/admin/conversations")]
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

    [HttpPost("{id}/messages/{messageId}/feedback")]
    [SwaggerOperation(Summary = "Submete (upsert) feedback do usuário em uma mensagem de assistente (like/dislike)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitMessageFeedback(
        string id,
        string messageId,
        [FromBody] SubmitMessageFeedbackRequest request,
        CancellationToken ct)
    {
        var user = _identityResolver.TryResolve(Request.Headers, out var errorMsg);
        if (user is null) return BadRequest(errorMsg);

        if (request is null)
            return BadRequest("Body inválido.");

        var result = await _facade.SubmitMessageFeedbackAsync(
            id, messageId, user.UserId, request.Sentiment, request.Comment, ct);

        if (result.Status != ConversationOperationStatus.Ok)
            return MapError(result.Status, result.ErrorMessage);

        return Ok(FeedbackToPayload(result.Value!));
    }

    [HttpGet("{id}/messages/{messageId}/feedback")]
    [SwaggerOperation(Summary = "Retorna o feedback do usuário corrente para uma mensagem (204 se não houver)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessageFeedback(
        string id, string messageId, CancellationToken ct)
    {
        var user = _identityResolver.TryResolve(Request.Headers, out var errorMsg);
        if (user is null) return BadRequest(errorMsg);

        var result = await _facade.GetMessageFeedbackAsync(id, messageId, user.UserId, ct);
        if (result.Status != ConversationOperationStatus.Ok)
            return MapError(result.Status, result.ErrorMessage);

        return result.Value is null ? NoContent() : Ok(FeedbackToPayload(result.Value));
    }

    [HttpDelete("{id}/messages/{messageId}/feedback")]
    [SwaggerOperation(Summary = "Remove o feedback do usuário corrente para uma mensagem")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMessageFeedback(
        string id, string messageId, CancellationToken ct)
    {
        var user = _identityResolver.TryResolve(Request.Headers, out var errorMsg);
        if (user is null) return BadRequest(errorMsg);

        var result = await _facade.DeleteMessageFeedbackAsync(id, messageId, user.UserId, ct);
        return result.Status == ConversationOperationStatus.Ok
            ? NoContent()
            : MapError(result.Status, result.ErrorMessage);
    }

    [HttpGet("/api/aihub/admin/message-feedbacks")]
    [SwaggerOperation(Summary = "Admin: lista feedbacks de mensagens com filtros (paginado). " +
        "Inclui executionId pra navegar até a execução/agente que respondeu.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMessageFeedbacksAdmin(
        [FromQuery] int? sentiment,
        [FromQuery] string? conversationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var (items, total) = await _facade.ListMessageFeedbacksAdminAsync(
            sentiment, conversationId, from, to, page, pageSize, ct);

        return Ok(new
        {
            items = items.Select(item =>
            {
                var fb = item.Feedback;
                return new
                {
                    feedbackId = fb.FeedbackId,
                    messageId = fb.MessageId,
                    conversationId = fb.ConversationId,
                    executionId = item.ExecutionId,
                    userId = fb.UserId,
                    sentiment = fb.Sentiment,
                    comment = fb.Comment,
                    createdAt = fb.CreatedAt,
                    updatedAt = fb.UpdatedAt
                };
            }),
            total,
            page,
            pageSize
        });
    }

    private static object FeedbackToPayload(MessageFeedback fb) => new
    {
        feedbackId = fb.FeedbackId,
        messageId = fb.MessageId,
        conversationId = fb.ConversationId,
        sentiment = fb.Sentiment,
        comment = fb.Comment,
        createdAt = fb.CreatedAt,
        updatedAt = fb.UpdatedAt
    };

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
