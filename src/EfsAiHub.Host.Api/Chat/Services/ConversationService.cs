using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.AgUi;
using EfsAiHub.Core.Abstractions.Identity;
using ChatMessage = EfsAiHub.Core.Abstractions.Conversations.ChatMessage;
using EfsAiHub.Core.Abstractions.Execution;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Operações de ciclo de vida da conversa (criar, deletar, limpar contexto).
/// </summary>
public interface IConversationLifecycle
{
    Task<ConversationSession> CreateAsync(
        string workflowId, string userId, string userType,
        Dictionary<string, string>? metadata = null, CancellationToken ct = default);

    Task DeleteAsync(string conversationId, CancellationToken ct = default);
    Task ClearContextAsync(string conversationId, CancellationToken ct = default);
}

/// <summary>
/// Operações de mensageria: envio de mensagens do usuário e callbacks de término
/// de execução do workflow.
/// </summary>
public interface IConversationMessaging
{
    Task<SendMessageResult> SendMessagesAsync(
        ConversationSession conversation,
        IReadOnlyList<ChatMessageInput> inputs,
        CancellationToken ct = default);

    Task OnExecutionCompletedAsync(
        string conversationId, string finalOutput, string executionId,
        string? lastActiveAgentId = null, CancellationToken ct = default);

    Task OnExecutionFailedAsync(
        string conversationId, string executionId, CancellationToken ct = default);

    Task OnRecoveryFailedAsync(
        string conversationId, string executionId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Orquestra a camada de chat. Dividido em partial files:
/// Lifecycle (Create/Delete/ClearContext) e Messaging (Send + callbacks).
/// </summary>
public partial class ConversationService : IConversationLifecycle, IConversationMessaging, IExecutionLifecycleObserver
{
    private readonly IConversationRepository _convRepo;
    private readonly IChatMessageRepository _msgRepo;
    private readonly IWorkflowDispatcher _workflowService;
    private readonly IWorkflowDefinitionRepository _workflowDefRepo;
    private readonly IHumanInteractionService _hitlService;
    private readonly TokenCountUpdater _tokenCountUpdater;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IAgUiSharedStateWriter? _sharedStateWriter;
    private readonly ILogger<ConversationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ConversationService(
        IConversationRepository convRepo,
        IChatMessageRepository msgRepo,
        IWorkflowDispatcher workflowService,
        IWorkflowDefinitionRepository workflowDefRepo,
        IHumanInteractionService hitlService,
        TokenCountUpdater tokenCountUpdater,
        IProjectContextAccessor projectAccessor,
        ILogger<ConversationService> logger,
        IAgUiSharedStateWriter? sharedStateWriter = null)
    {
        _convRepo = convRepo;
        _msgRepo = msgRepo;
        _workflowService = workflowService;
        _workflowDefRepo = workflowDefRepo;
        _hitlService = hitlService;
        _tokenCountUpdater = tokenCountUpdater;
        _projectAccessor = projectAccessor;
        _sharedStateWriter = sharedStateWriter;
        _logger = logger;
    }

    // ── Helpers internos compartilhados ───────────────────────────────────────

    /// <summary>
    /// Constrói a entidade ChatMessage a partir do input. Resolução robot↔role↔actor:
    ///   - Caller já passou actor=Robot explicitamente OU role legado="robot" → Role=user, Actor=Robot.
    ///   - Demais casos: Role= input.Role lowercased, Actor=Human.
    /// Manter Role=user em mensagens robot preserva os 5 canônicos AG-UI; o discriminador
    /// fica no campo Actor. Ver ADR 0014.
    /// </summary>
    private static ChatMessage BuildChatMessage(string conversationId, ChatMessageInput input)
    {
        var legacyRobotRole = input.Role.Equals("robot", StringComparison.OrdinalIgnoreCase);
        var actor = input.Actor == Actor.Robot || legacyRobotRole ? Actor.Robot : Actor.Human;
        var role = legacyRobotRole ? "user" : input.Role.ToLowerInvariant();

        return new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            Role = role,
            Content = input.Message,
            TokenCount = 0,
            Actor = actor
        };
    }

    private async Task<ChatTurnContext> BuildTurnContextAsync(
        ConversationSession conversation,
        ChatMessageInput currentMessage,
        IReadOnlyList<ChatMessage> history)
    {
        var metadata = new Dictionary<string, string>(conversation.Metadata)
        {
            ["userId"] = conversation.UserId
        };
        if (conversation.UserType is not null)
            metadata["userType"] = conversation.UserType;

        // Carrega shared state (agent drafts) do AG-UI state store
        JsonElement? sharedState = null;
        if (_sharedStateWriter is not null)
        {
            try
            {
                sharedState = await _sharedStateWriter.GetSnapshotAsync(conversation.ConversationId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ConvService] Falha ao carregar shared state para conversa '{ConvId}'.",
                    conversation.ConversationId);
            }
        }

        return new ChatTurnContext
        {
            UserId = conversation.UserId,
            ConversationId = conversation.ConversationId,
            Message = new ChatTurnMessage { Role = currentMessage.Role, Content = currentMessage.Message },
            History = history.Select(m => new ChatTurnMessage
            {
                Role = m.Role,
                Content = m.Content,
                Output = m.StructuredOutput?.RootElement.Clone()
            }).ToList(),
            Metadata = metadata,
            SharedState = sharedState
        };
    }

    /// <summary>
    /// Remove as mensagens mais antigas do histórico até que o total de tokens
    /// estimado caiba no budget. Usa TokenCount persistido; fallback: Content.Length / 4.
    /// </summary>
    private static List<ChatMessage> TrimHistoryByTokenBudget(List<ChatMessage> history, int maxTokens)
    {
        // Acumula de trás para frente (mensagens mais recentes primeiro)
        var totalTokens = 0;
        var startIndex = history.Count;

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var tokens = EstimateTokens(history[i]);
            if (totalTokens + tokens > maxTokens)
                break;
            totalTokens += tokens;
            startIndex = i;
        }

        return startIndex == 0 ? history : history.GetRange(startIndex, history.Count - startIndex);
    }

    private static int EstimateTokens(ChatMessage msg)
    {
        if (msg.TokenCount > 0) return msg.TokenCount;
        // Fallback: ~4 caracteres por token (heurística conservadora)
        return Math.Max(1, msg.Content.Length / 4);
    }

    private static string TruncateTitle(string message)
        => message.Length <= 50 ? message : string.Concat(message.AsSpan(0, 47), "...");

    private static void UpdateConversationTitle(
        ConversationSession conversation, IReadOnlyList<ChatMessage> msgs)
    {
        if (conversation.Title is not null) return;
        // Robot mensagens carregam payload programático (ex: JSON de chamada externa)
        // que não faz sentido virar título da conversa. Filtra Actor=Human.
        var first = msgs.FirstOrDefault(m => m.Role == "user" && m.Actor == Actor.Human);
        if (first is not null)
            conversation.Title = TruncateTitle(first.Content);
    }
}

/// <summary>
/// Input de mensagem vindo do request body do endpoint. <see cref="Actor"/> default
/// <see cref="Conversations.Actor.Human"/>; robot é setado explicitamente pelo caller
/// que processou o body AG-UI ou (legado) pelo controller que aceita role="robot".
/// </summary>
public record ChatMessageInput(string Role, string Message, Actor Actor = Actor.Human);

/// <summary>Resultado de SendMessagesAsync.</summary>
public record SendMessageResult(
    string? ExecutionId,
    bool HitlResolved,
    IReadOnlyList<ChatMessage>? PersistedMessages,
    string? TooEarlyReason = null);
