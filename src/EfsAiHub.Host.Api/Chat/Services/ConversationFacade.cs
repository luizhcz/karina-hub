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
        CancellationToken ct);

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
}

public sealed class ConversationFacade : IConversationFacade
{
    private readonly ConversationService _conversationService;
    private readonly IConversationRepository _convRepo;
    private readonly IChatMessageRepository _msgRepo;
    private readonly IWorkflowDefinitionRepository _workflowDefRepo;
    private readonly ChatRateLimiter _rateLimiter;
    private readonly ConversationLockManager _lockManager;
    private readonly ChatRoutingOptions _routing;

    public ConversationFacade(
        ConversationService conversationService,
        IConversationRepository convRepo,
        IChatMessageRepository msgRepo,
        IWorkflowDefinitionRepository workflowDefRepo,
        ChatRateLimiter rateLimiter,
        ConversationLockManager lockManager,
        IOptions<ChatRoutingOptions> routing)
    {
        _conversationService = conversationService;
        _convRepo = convRepo;
        _msgRepo = msgRepo;
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
        CancellationToken ct)
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
            var result = await _conversationService.SendMessagesAsync(session, inputs, ct);
            return ConversationOperationResult<SendMessageResult>.Success(result);
        }
        catch (ChatBackPressureException ex)
        {
            return ConversationOperationResult<SendMessageResult>.RateLimited(ex.Message);
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
}
