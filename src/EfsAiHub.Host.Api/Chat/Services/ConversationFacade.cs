using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Execution;
using ChatMessage = EfsAiHub.Core.Abstractions.Conversations.ChatMessage;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Resultado unificado de chamadas da facade que podem falhar com códigos HTTP específicos
/// sem acoplar a camada de aplicação ao ASP.NET. O controller mapeia para IActionResult.
/// </summary>
public enum ConversationOperationStatus
{
    Ok,
    NotFound,
    BadRequest,
    RateLimited,
    Conflict
}

public record ConversationOperationResult<T>(
    ConversationOperationStatus Status,
    T? Value,
    string? ErrorMessage)
{
    public static ConversationOperationResult<T> Success(T value) =>
        new(ConversationOperationStatus.Ok, value, null);

    public static ConversationOperationResult<T> NotFound(string message) =>
        new(ConversationOperationStatus.NotFound, default, message);

    public static ConversationOperationResult<T> BadRequest(string message) =>
        new(ConversationOperationStatus.BadRequest, default, message);

    public static ConversationOperationResult<T> RateLimited(string message) =>
        new(ConversationOperationStatus.RateLimited, default, message);
}

/// <summary>
/// Fachada das operações de conversa. Encapsula repositórios, workflow definition,
/// rate limiter e routing para que o controller receba apenas uma dependência funcional
/// além do que é estritamente HTTP (identity resolution, SSE event bus).
/// </summary>
public interface IConversationFacade
{
    Task<ConversationOperationResult<ConversationSession>> CreateAsync(
        string? explicitWorkflowId,
        string userId,
        string userType,
        Dictionary<string, string>? metadata,
        CancellationToken ct);

    Task<ConversationSession?> GetAsync(string conversationId, CancellationToken ct);

    Task<ConversationOperationResult<IReadOnlyList<ChatMessage>>> ListMessagesAsync(
        string conversationId,
        int limit,
        int offset,
        CancellationToken ct);

    Task<ConversationOperationResult<SendMessageResult>> SendMessagesAsync(
        string conversationId,
        string userId,
        IReadOnlyList<ChatMessageInput> inputs,
        CancellationToken ct,
        string? workflowVersionId = null,
        IReadOnlyList<ChatMessageInput>? echoHistory = null);

    Task<ConversationOperationResult<bool>> DeleteAsync(string conversationId, CancellationToken ct);

    Task<ConversationOperationResult<bool>> ClearContextAsync(string conversationId, CancellationToken ct);

    Task<(IReadOnlyList<ConversationSession> Items, int Total)> ListAllAsync(
        string? userId,
        string? workflowId,
        string? projectId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<ConversationOperationResult<MessageFeedback>> SubmitMessageFeedbackAsync(
        string conversationId,
        string messageId,
        string userId,
        int sentiment,
        string? comment,
        CancellationToken ct);

    Task<ConversationOperationResult<MessageFeedback?>> GetMessageFeedbackAsync(
        string conversationId,
        string messageId,
        string userId,
        CancellationToken ct);

    Task<ConversationOperationResult<bool>> DeleteMessageFeedbackAsync(
        string conversationId,
        string messageId,
        string userId,
        CancellationToken ct);

    Task<(IReadOnlyList<MessageFeedbackAdminItem> Items, int Total)> ListMessageFeedbacksAdminAsync(
        int? sentiment,
        string? conversationId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct);
}

/// <summary>
/// Item de listagem admin: feedback + ExecutionId da mensagem (resolvido por
/// batch lookup) pra que o admin possa navegar até a execução / agente sem
/// uma chamada extra por feedback.
/// </summary>
public record MessageFeedbackAdminItem(MessageFeedback Feedback, string? ExecutionId);

public sealed class ConversationFacade : IConversationFacade
{
    private readonly ConversationService _conversationService;
    private readonly IConversationRepository _convRepo;
    private readonly IChatMessageRepository _msgRepo;
    private readonly IMessageFeedbackRepository _feedbackRepo;
    private readonly IWorkflowDefinitionRepository _workflowDefRepo;
    private readonly ChatRateLimiter _rateLimiter;
    private readonly ConversationLockManager _lockManager;
    private readonly ChatRoutingOptions _routing;

    public ConversationFacade(
        ConversationService conversationService,
        IConversationRepository convRepo,
        IChatMessageRepository msgRepo,
        IMessageFeedbackRepository feedbackRepo,
        IWorkflowDefinitionRepository workflowDefRepo,
        ChatRateLimiter rateLimiter,
        ConversationLockManager lockManager,
        IOptions<ChatRoutingOptions> routing)
    {
        _conversationService = conversationService;
        _convRepo = convRepo;
        _msgRepo = msgRepo;
        _feedbackRepo = feedbackRepo;
        _workflowDefRepo = workflowDefRepo;
        _rateLimiter = rateLimiter;
        _lockManager = lockManager;
        _routing = routing.Value;
    }

    public async Task<ConversationOperationResult<ConversationSession>> CreateAsync(
        string? explicitWorkflowId,
        string userId,
        string userType,
        Dictionary<string, string>? metadata,
        CancellationToken ct)
    {
        var workflowId = explicitWorkflowId;
        if (string.IsNullOrWhiteSpace(workflowId) &&
            !_routing.DefaultWorkflows.TryGetValue(userType, out workflowId))
        {
            return ConversationOperationResult<ConversationSession>.BadRequest(
                $"Nenhum workflow padrão configurado para userType='{userType}'. Informe 'workflowId' no body.");
        }

        var workflowDef = await _workflowDefRepo.GetByIdAsync(workflowId!, ct);
        if (workflowDef is null)
            return ConversationOperationResult<ConversationSession>.NotFound($"Workflow '{workflowId}' não encontrado.");

        if (!workflowDef.Configuration.InputMode.Equals("Chat", StringComparison.OrdinalIgnoreCase))
            return ConversationOperationResult<ConversationSession>.BadRequest(
                $"Workflow '{workflowId}' não está em modo Chat (InputMode={workflowDef.Configuration.InputMode}).");

        var session = await _conversationService.CreateAsync(workflowId!, userId, userType, metadata, ct);
        return ConversationOperationResult<ConversationSession>.Success(session);
    }

    public Task<ConversationSession?> GetAsync(string conversationId, CancellationToken ct)
        => _convRepo.GetByIdAsync(conversationId, ct);

    public async Task<ConversationOperationResult<IReadOnlyList<ChatMessage>>> ListMessagesAsync(
        string conversationId, int limit, int offset, CancellationToken ct)
    {
        if (await _convRepo.GetByIdAsync(conversationId, ct) is null)
            return ConversationOperationResult<IReadOnlyList<ChatMessage>>.NotFound(
                $"Conversa '{conversationId}' não encontrada.");

        var messages = await _msgRepo.ListAsync(conversationId, limit, offset, ct);
        return ConversationOperationResult<IReadOnlyList<ChatMessage>>.Success(messages);
    }

    public async Task<ConversationOperationResult<SendMessageResult>> SendMessagesAsync(
        string conversationId,
        string userId,
        IReadOnlyList<ChatMessageInput> inputs,
        CancellationToken ct,
        string? workflowVersionId = null,
        IReadOnlyList<ChatMessageInput>? echoHistory = null)
    {
        if (inputs.Count == 0)
            return ConversationOperationResult<SendMessageResult>.BadRequest("A lista de mensagens não pode ser vazia.");

        if (!await _rateLimiter.TryAcquireAsync(userId, ct))
            return ConversationOperationResult<SendMessageResult>.RateLimited(
                "Limite de mensagens excedido. Tente novamente em breve.");

        if (!await _rateLimiter.TryAcquireForConversationAsync(conversationId, ct))
            return ConversationOperationResult<SendMessageResult>.RateLimited(
                "Limite de mensagens por conversa excedido. Tente novamente em breve.");

        // Lock por conversa: evita race condition de envio concorrente
        // (dois requests disparando dois workflows simultâneos)
        using var _ = await _lockManager.AcquireAsync(conversationId, ct);

        var session = await _convRepo.GetByIdAsync(conversationId, ct);
        if (session is null)
            return ConversationOperationResult<SendMessageResult>.NotFound(
                $"Conversa '{conversationId}' não encontrada.");

        try
        {
            var result = await _conversationService.SendMessagesAsync(session, inputs, ct, workflowVersionId, echoHistory);
            return ConversationOperationResult<SendMessageResult>.Success(result);
        }
        catch (ChatBackPressureException ex)
        {
            return ConversationOperationResult<SendMessageResult>.RateLimited(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return ConversationOperationResult<SendMessageResult>.NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConversationOperationResult<SendMessageResult>.BadRequest(ex.Message);
        }
    }

    public async Task<ConversationOperationResult<bool>> DeleteAsync(string conversationId, CancellationToken ct)
    {
        try
        {
            await _conversationService.DeleteAsync(conversationId, ct);
            return ConversationOperationResult<bool>.Success(true);
        }
        catch (KeyNotFoundException)
        {
            return ConversationOperationResult<bool>.NotFound($"Conversa '{conversationId}' não encontrada.");
        }
    }

    public async Task<ConversationOperationResult<bool>> ClearContextAsync(string conversationId, CancellationToken ct)
    {
        if (await _convRepo.GetByIdAsync(conversationId, ct) is null)
            return ConversationOperationResult<bool>.NotFound($"Conversa '{conversationId}' não encontrada.");

        await _conversationService.ClearContextAsync(conversationId, ct);
        return ConversationOperationResult<bool>.Success(true);
    }

    public async Task<(IReadOnlyList<ConversationSession> Items, int Total)> ListAllAsync(
        string? userId, string? workflowId, string? projectId, DateTime? from, DateTime? to,
        int page, int pageSize, CancellationToken ct)
    {
        var items = await _convRepo.GetAllAsync(userId, workflowId, projectId, from, to, page, pageSize, ct);
        var total = await _convRepo.CountAllAsync(userId, workflowId, projectId, from, to, ct);
        return (items, total);
    }

    public async Task<ConversationOperationResult<MessageFeedback>> SubmitMessageFeedbackAsync(
        string conversationId,
        string messageId,
        string userId,
        int sentiment,
        string? comment,
        CancellationToken ct)
    {
        if (sentiment != 1 && sentiment != -1)
            return ConversationOperationResult<MessageFeedback>.BadRequest(
                "Sentiment deve ser +1 (like) ou -1 (dislike).");

        var validation = await ValidateConversationAndMessageAsync(conversationId, messageId, userId, ct);
        if (validation.Result is not null)
            return validation.Result.Cast<MessageFeedback>();

        var feedback = new MessageFeedback
        {
            FeedbackId = Guid.NewGuid().ToString("N"),
            MessageId = messageId,
            ConversationId = conversationId,
            UserId = userId,
            Sentiment = sentiment,
            Comment = comment,
            ProjectId = validation.Conversation!.ProjectId
        };

        var saved = await _feedbackRepo.UpsertAsync(feedback, ct);
        return ConversationOperationResult<MessageFeedback>.Success(saved);
    }

    public async Task<ConversationOperationResult<MessageFeedback?>> GetMessageFeedbackAsync(
        string conversationId,
        string messageId,
        string userId,
        CancellationToken ct)
    {
        var validation = await ValidateConversationAndMessageAsync(conversationId, messageId, userId, ct);
        if (validation.Result is not null)
            return validation.Result.Cast<MessageFeedback?>();

        var feedback = await _feedbackRepo.GetByUserAndMessageAsync(userId, messageId, ct);
        return ConversationOperationResult<MessageFeedback?>.Success(feedback);
    }

    public async Task<ConversationOperationResult<bool>> DeleteMessageFeedbackAsync(
        string conversationId,
        string messageId,
        string userId,
        CancellationToken ct)
    {
        var validation = await ValidateConversationAndMessageAsync(conversationId, messageId, userId, ct);
        if (validation.Result is not null)
            return validation.Result.Cast<bool>();

        var removed = await _feedbackRepo.DeleteByUserAndMessageAsync(userId, messageId, ct);
        return removed
            ? ConversationOperationResult<bool>.Success(true)
            : ConversationOperationResult<bool>.NotFound("Feedback não encontrado.");
    }

    public async Task<(IReadOnlyList<MessageFeedbackAdminItem> Items, int Total)> ListMessageFeedbacksAdminAsync(
        int? sentiment, string? conversationId, DateTime? from, DateTime? to,
        int page, int pageSize, CancellationToken ct)
    {
        var items = await _feedbackRepo.ListAdminAsync(sentiment, conversationId, from, to, page, pageSize, ct);
        var total = await _feedbackRepo.CountAdminAsync(sentiment, conversationId, from, to, ct);

        // Enriquece com ExecutionId via batch lookup. Permite ao admin navegar
        // pra execução/agente que produziu a mensagem sem chamada extra por item.
        var messageIds = items.Select(f => f.MessageId).Distinct().ToList();
        var messages = await _msgRepo.GetByIdsAsync(messageIds, ct);
        var executionByMessage = messages.ToDictionary(m => m.MessageId, m => m.ExecutionId);

        var enriched = items
            .Select(f => new MessageFeedbackAdminItem(
                f,
                executionByMessage.TryGetValue(f.MessageId, out var execId) ? execId : null))
            .ToList();

        return (enriched, total);
    }

    private readonly record struct FeedbackValidation(
        ConversationSession? Conversation,
        ChatMessage? Message,
        FeedbackErrorResult? Result);

    private async Task<FeedbackValidation> ValidateConversationAndMessageAsync(
        string conversationId, string messageId, string userId, CancellationToken ct)
    {
        var conversation = await _convRepo.GetByIdAsync(conversationId, ct);
        // Não vaza existência de conversa de outro usuário: trata como NotFound.
        if (conversation is null || conversation.UserId != userId)
            return new FeedbackValidation(null, null,
                FeedbackErrorResult.NotFound($"Conversa '{conversationId}' não encontrada."));

        var message = await _msgRepo.GetByIdAsync(messageId, ct);
        if (message is null || message.ConversationId != conversationId)
            return new FeedbackValidation(conversation, null,
                FeedbackErrorResult.NotFound($"Mensagem '{messageId}' não encontrada na conversa."));

        if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            return new FeedbackValidation(conversation, message,
                FeedbackErrorResult.BadRequest("Feedback é permitido apenas em mensagens de assistente."));

        return new FeedbackValidation(conversation, message, null);
    }
}

internal record FeedbackErrorResult(ConversationOperationStatus Status, string Message)
{
    public static FeedbackErrorResult NotFound(string msg) => new(ConversationOperationStatus.NotFound, msg);
    public static FeedbackErrorResult BadRequest(string msg) => new(ConversationOperationStatus.BadRequest, msg);

    public ConversationOperationResult<T> Cast<T>() => Status switch
    {
        ConversationOperationStatus.NotFound => ConversationOperationResult<T>.NotFound(Message),
        ConversationOperationStatus.BadRequest => ConversationOperationResult<T>.BadRequest(Message),
        _ => ConversationOperationResult<T>.BadRequest(Message)
    };
}
