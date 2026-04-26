using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using ChatMessage = EfsAiHub.Core.Abstractions.Conversations.ChatMessage;
using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Envio de mensagens do usuário, disparo de workflows e callbacks de término
/// chamados por <see cref="Infrastructure.BackgroundServices.ExecutionFailureWriter"/>.
/// </summary>
public partial class ConversationService
{
    public async Task<SendMessageResult> SendMessagesAsync(
        ConversationSession conversation,
        IReadOnlyList<ChatMessageInput> inputs,
        CancellationToken ct = default)
    {
        if (inputs.Count == 0)
            return new SendMessageResult(null, false, null);

        var lastInput = inputs[^1];
        // Caller pode discriminar via campo tipado Actor (caminho novo do wire AG-UI)
        // ou via role legado "robot" (compat com controller antigo). Ver ADR 0014.
        bool lastIsRobot = lastInput.Actor == Actor.Robot
                        || lastInput.Role.Equals("robot", StringComparison.OrdinalIgnoreCase);

        var persisted = inputs.Select(input => BuildChatMessage(conversation.ConversationId, input)).ToList();
        await _msgRepo.SaveBatchAsync(persisted, ct);

        if (lastIsRobot)
        {
            UpdateConversationTitle(conversation, persisted);
            conversation.LastMessageAt = persisted.Last().CreatedAt;
            await _convRepo.UpdateAsync(conversation, ct);
            return new SendMessageResult(null, false, persisted);
        }

        if (!string.IsNullOrEmpty(conversation.ActiveExecutionId))
        {
            var pendingHitl = _hitlService.GetPendingForExecution(conversation.ActiveExecutionId);
            if (pendingHitl is not null)
            {
                var userContent = lastInput.Message;
                // ResolvedBy = userId da conversa. Em chat context a conversa sempre tem UserId
                // definido (criada via facade). Propagação automática para auditoria HITL.
                await _hitlService.ResolveAsync(
                    pendingHitl.InteractionId,
                    userContent,
                    resolvedBy: conversation.UserId,
                    approved: true,
                    ct: ct);

                _logger.LogInformation(
                    "[ConvService] HITL '{InteractionId}' resolvido pela mensagem do chat '{ConvId}'.",
                    pendingHitl.InteractionId, conversation.ConversationId);

                conversation.LastMessageAt = DateTime.UtcNow;
                await _convRepo.UpdateAsync(conversation, ct);
                return new SendMessageResult(conversation.ActiveExecutionId, true, persisted);
            }

            var exec = await _workflowService.GetExecutionAsync(conversation.ActiveExecutionId, ct);
            if (exec?.Status is WorkflowStatus.Running or WorkflowStatus.Pending or WorkflowStatus.Paused)
            {
                try
                {
                    await _workflowService.CancelExecutionAsync(conversation.ActiveExecutionId, ct);
                    _logger.LogInformation(
                        "[ConvService] Execução anterior '{ExecId}' cancelada automaticamente para conversa '{ConvId}'.",
                        conversation.ActiveExecutionId, conversation.ConversationId);

                    // Aguarda transição para estado terminal antes de disparar nova execução.
                    // Evita race onde OnExecutionCompletedAsync da execução antiga chega
                    // depois da nova e sobrescreve/duplica mensagens.
                    await WaitForTerminalStatusAsync(
                        conversation.ActiveExecutionId, TimeSpan.FromSeconds(2), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[ConvService] Falha ao cancelar execução '{ExecId}' — prosseguindo com nova execução.",
                        conversation.ActiveExecutionId);
                }
            }

            conversation.ActiveExecutionId = null;
        }

        return await TriggerWorkflowAsync(conversation, lastInput, persisted, ct);
    }

    private async Task<SendMessageResult> TriggerWorkflowAsync(
        ConversationSession conversation,
        ChatMessageInput lastInput,
        List<ChatMessage> persisted,
        CancellationToken ct)
    {
        var workflowDef = await _workflowDefRepo.GetByIdAsync(conversation.WorkflowId, ct);
        var config = workflowDef?.Configuration;
        var maxHistory = config?.MaxHistoryMessages ?? 20;
        var maxHistoryTokens = config?.MaxHistoryTokens;

        var history = await _msgRepo.GetContextWindowAsync(
            conversation.ConversationId,
            maxMessages: maxHistory,
            sinceUtc: conversation.ContextClearedAt,
            ct: ct);

        var historyWithoutCurrent = history
            .Where(m => !persisted.Any(p => p.MessageId == m.MessageId))
            .ToList();

        // Token-aware trimming: remove as mensagens mais antigas até caber no budget
        if (maxHistoryTokens is > 0)
            historyWithoutCurrent = TrimHistoryByTokenBudget(historyWithoutCurrent, maxHistoryTokens.Value);

        var turnContext = await BuildTurnContextAsync(conversation, lastInput, historyWithoutCurrent);
        var contextJson = JsonSerializer.Serialize(turnContext, JsonOpts);

        var triggerMetadata = new Dictionary<string, string>
        {
            ["conversationId"] = conversation.ConversationId,
            ["userId"] = conversation.UserId,
            ["userType"] = conversation.UserType ?? "",
            // F4: ProjectId do scope atual flui pro worker via metadata;
            // lido em WorkflowRunnerService e posto em ExecutionContext.ProjectId.
            ["projectId"] = conversation.ProjectId
        };

        if (!string.IsNullOrEmpty(conversation.LastActiveAgentId))
            triggerMetadata["startAgentId"] = conversation.LastActiveAgentId;

        var executionId = await _workflowService.TriggerAsync(
            conversation.WorkflowId,
            contextJson,
            triggerMetadata,
            source: EfsAiHub.Core.Abstractions.Execution.ExecutionSource.Chat,
            ct: ct);

        conversation.ActiveExecutionId = executionId;
        conversation.LastMessageAt = DateTime.UtcNow;
        if (conversation.Title is null)
            conversation.Title = TruncateTitle(lastInput.Message);

        await _convRepo.UpdateAsync(conversation, ct);

        _logger.LogInformation("[ConvService] Workflow '{WorkflowId}' disparado (exec: {ExecId}) para conversa '{ConvId}'.",
            conversation.WorkflowId, executionId, conversation.ConversationId);

        return new SendMessageResult(executionId, false, persisted);
    }

    public async Task OnExecutionCompletedAsync(
        string conversationId, string finalOutput, string executionId,
        string? lastActiveAgentId = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ConvService] OnExecutionCompletedAsync — convId='{ConvId}', execId='{ExecId}'.",
            conversationId, executionId);

        var conversation = await _convRepo.GetByIdAsync(conversationId, ct);
        if (conversation is null) return;

        // Validação idempotente: só persiste a resposta se a execução ainda é a ativa.
        // Paridade com OnExecutionFailedAsync — evita que um completion atrasado de uma
        // execução cancelada sobrescreva a resposta de uma nova execução já em curso.
        if (conversation.ActiveExecutionId != executionId)
        {
            MetricsRegistry.StaleExecutionCompletionSkipped.Add(1);
            _logger.LogWarning(
                "[ConvService] Ignorando completion de execução '{ExecId}' — conversa '{ConvId}' " +
                "já tem ActiveExecutionId='{ActiveId}'.",
                executionId, conversationId, conversation.ActiveExecutionId);
            return;
        }

        var parsed = ExecutionOutputParser.Parse(finalOutput);

        var assistantMsg = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            Role = "assistant",
            Content = parsed.TextContent,
            StructuredOutput = parsed.StructuredOutput,
            TokenCount = 0,
            ExecutionId = executionId
        };

        await _msgRepo.SaveAsync(assistantMsg, ct);

        // Atualiza TokenCount com valor real de llm_token_usage (fire-and-forget)
        _tokenCountUpdater.EnqueueUpdate(assistantMsg.MessageId, executionId);

        conversation.ActiveExecutionId = null;
        conversation.LastActiveAgentId = lastActiveAgentId ?? conversation.LastActiveAgentId;
        conversation.LastMessageAt = assistantMsg.CreatedAt;

        await _convRepo.UpdateAsync(conversation, ct);

        _logger.LogInformation(
            "[ConvService] Resposta do assistente persistida para conversa '{ConvId}'. LastActiveAgent='{AgentId}'.",
            conversationId, conversation.LastActiveAgentId);
    }

    private async Task WaitForTerminalStatusAsync(string executionId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var exec = await _workflowService.GetExecutionAsync(executionId, ct);
            if (exec is null) return;
            if (exec.Status is WorkflowStatus.Completed
                             or WorkflowStatus.Failed
                             or WorkflowStatus.Cancelled)
                return;

            try { await Task.Delay(TimeSpan.FromMilliseconds(50), ct); }
            catch (OperationCanceledException) { return; }
        }

        _logger.LogWarning(
            "[ConvService] Timeout aguardando estado terminal de '{ExecId}'. Prosseguindo mesmo assim.",
            executionId);
    }

    /// <summary>
    /// Callback do HitlRecoveryService quando a retomada de uma execução HITL
    /// falha (checkpoint corrompido, erro no resume, etc). Posta uma mensagem
    /// de sistema ao usuário e limpa a execução ativa da conversa.
    /// </summary>
    public async Task OnRecoveryFailedAsync(
        string conversationId, string executionId, string reason, CancellationToken ct = default)
    {
        var conversation = await _convRepo.GetByIdAsync(conversationId, ct);
        if (conversation is null) return;

        var assistantMsg = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            Role = "assistant",
            Content = "Desculpe, a operação anterior foi perdida devido a uma reinicialização. Pode repetir, por favor?",
            TokenCount = 0,
            ExecutionId = executionId
        };

        await _msgRepo.SaveAsync(assistantMsg, ct);

        if (conversation.ActiveExecutionId == executionId)
            conversation.ActiveExecutionId = null;
        conversation.LastMessageAt = assistantMsg.CreatedAt;
        await _convRepo.UpdateAsync(conversation, ct);

        _logger.LogWarning(
            "[ConvService] Recovery HITL falhou para execução '{ExecId}' em conversa '{ConvId}'. Motivo: {Reason}",
            executionId, conversationId, reason);
    }

    public async Task OnExecutionFailedAsync(
        string conversationId, string executionId, CancellationToken ct = default)
    {
        var conversation = await _convRepo.GetByIdAsync(conversationId, ct);
        if (conversation is null) return;

        // Limpa apenas se corresponde à execução que falhou (evita race com execuções mais recentes)
        if (conversation.ActiveExecutionId == executionId)
        {
            conversation.ActiveExecutionId = null;
            await _convRepo.UpdateAsync(conversation, ct);
            _logger.LogInformation(
                "[ConvService] ActiveExecutionId limpo para conversa '{ConvId}' após falha/cancelamento da execução '{ExecId}'.",
                conversationId, executionId);
        }
    }
}
