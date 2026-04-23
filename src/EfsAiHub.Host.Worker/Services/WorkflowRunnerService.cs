using System.Text.Json;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Platform.Runtime;
using EfsAiHub.Platform.Runtime.Checkpointing;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Executa um único workflow como Task independente.
/// Processa o stream de eventos do framework e os mapeia para o domínio da aplicação.
/// Inclui rastreamento por nó (per-node tracking) com persistência em Postgres.
/// </summary>
/// <summary>
/// Agrupa repositórios usados pelo WorkflowRunnerService para reduzir arity do construtor.
/// </summary>
public sealed record WorkflowRunnerRepositories(
    IWorkflowExecutionRepository Executions,
    INodeExecutionRepository Nodes);

/// <summary>
/// Agrupa serviços auxiliares do WorkflowRunnerService (eventos, tokens, HITL, persistência).
/// </summary>
public sealed record WorkflowRunnerCollaborators(
    IWorkflowEventBus EventBus,
    TokenBatcher TokenBatcher,
    IHumanInteractionService HitlService,
    ExecutionFailureWriter FailureWriter,
    NodePersistenceService NodePersistence,
    IEngineCheckpointAdapter CheckpointAdapter,
    EventHandlers.AgentHandoffEventHandler AgentHandoffHandler,
    EfsAiHub.Core.Abstractions.AgUi.IAgUiSharedStateWriter? SharedStateWriter = null,
    // Opcional: se registrado, runner resolve Persona a partir do UserId e passa
    // pro ExecutionContext. Null desabilita personalização (fallback ao prompt base).
    EfsAiHub.Core.Abstractions.Identity.Persona.IPersonaProvider? PersonaProvider = null);

public class WorkflowRunnerService
{
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly INodeExecutionRepository _nodeRepo;
    private readonly IWorkflowEventBus _eventBus;
    private readonly TokenBatcher _tokenBatcher;
    private readonly IHumanInteractionService _hitlService;
    private readonly ExecutionFailureWriter _failureWriter;
    private readonly NodePersistenceService _nodePersistence;
    private readonly IEngineCheckpointAdapter _checkpointAdapter;
    private readonly EventHandlers.AgentHandoffEventHandler _agentHandoffHandler;
    private readonly EfsAiHub.Core.Abstractions.AgUi.IAgUiSharedStateWriter? _sharedStateWriter;
    private readonly EfsAiHub.Core.Abstractions.Identity.Persona.IPersonaProvider? _personaProvider;
    private readonly ILogger<WorkflowRunnerService> _logger;

    public WorkflowRunnerService(
        WorkflowRunnerRepositories repositories,
        WorkflowRunnerCollaborators collaborators,
        ILogger<WorkflowRunnerService> logger)
    {
        _executionRepo = repositories.Executions;
        _nodeRepo = repositories.Nodes;
        _eventBus = collaborators.EventBus;
        _tokenBatcher = collaborators.TokenBatcher;
        _hitlService = collaborators.HitlService;
        _failureWriter = collaborators.FailureWriter;
        _nodePersistence = collaborators.NodePersistence;
        _checkpointAdapter = collaborators.CheckpointAdapter;
        _agentHandoffHandler = collaborators.AgentHandoffHandler;
        _sharedStateWriter = collaborators.SharedStateWriter;
        _personaProvider = collaborators.PersonaProvider;
        _logger = logger;
    }

    public async Task RunAsync(
        WorkflowExecution execution,
        object workflow,
        int timeoutSeconds,
        int maxAgentInvocations,
        int maxTokensPerExecution,
        decimal? maxCostUsdPerExecution,
        EfsAiHub.Core.Agents.Execution.AccountGuardMode guardMode,
        IReadOnlyDictionary<string, string>? agentNames,
        OrchestrationMode orchestrationMode,
        IReadOnlyList<EfsAiHub.Core.Agents.Enrichment.EnrichmentRule>? enrichmentRules,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        using var activity = ActivitySources.WorkflowExecutionSource.StartActivity("WorkflowRun");
        activity?.SetTag("workflow.id", execution.WorkflowId);
        activity?.SetTag("execution.id", execution.ExecutionId);

        execution.Status = WorkflowStatus.Running;
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);

        // Publica workflow_started para o SSE antes de processar eventos do workflow
        await _eventBus.PublishAsync(execution.ExecutionId, new WorkflowEventEnvelope
        {
            EventType = "workflow_started",
            ExecutionId = execution.ExecutionId,
            Payload = JsonSerializer.Serialize(new { workflowId = execution.WorkflowId, timestamp = DateTime.UtcNow })
        });

        var workflowTag = new KeyValuePair<string, object?>("workflow.id", execution.WorkflowId);
        MetricsRegistry.WorkflowsTriggered.Add(1, workflowTag);
        MetricsRegistry.ActiveExecutions.Add(1, workflowTag);

        // ── Estado de rastreamento por nó ─────────────────────────────────────
        await using var nodeTracker = new NodeStateTracker();

        // Extrai userId do ChatTurnContext (se Chat mode) para AccountGuard em tool calls.
        // Também reaproveita o mesmo userId para resolver Persona (personalização de prompts).
        string? guardUserId = null;
        string? guardUserType = null;
        if (!string.IsNullOrEmpty(execution.Input))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(execution.Input);
                if (doc.RootElement.TryGetProperty("userId", out var uidEl))
                    guardUserId = uidEl.GetString();
                // userType vem via metadata["userType"] no payload do ChatTurnContext
                if (doc.RootElement.TryGetProperty("metadata", out var metaEl)
                    && metaEl.ValueKind == JsonValueKind.Object
                    && metaEl.TryGetProperty("userType", out var utEl))
                    guardUserType = utEl.GetString();
            }
            catch { /* Input não é ChatTurnContext — fica anonymous */ }
        }

        // Resolução de Persona (silent fallback — contrato nunca lança)
        EfsAiHub.Core.Abstractions.Identity.Persona.UserPersona? persona = null;
        if (_personaProvider is not null && !string.IsNullOrWhiteSpace(guardUserId))
        {
            persona = await _personaProvider.ResolveAsync(
                guardUserId, guardUserType ?? "cliente", ct);
        }

        // Extrai conversationId da metadata (Chat executions) para shared state
        string? conversationId = null;
        execution.Metadata?.TryGetValue("conversationId", out conversationId);

        // Delegate para tools atualizarem AG-UI shared state (agent drafts)
        Func<string, System.Text.Json.JsonElement, Task>? updateSharedState = null;
        if (_sharedStateWriter is not null && conversationId is not null)
        {
            var writer = _sharedStateWriter;
            var threadId = conversationId;
            var execId = execution.ExecutionId;
            var eventBus = _eventBus;
            updateSharedState = async (path, value) =>
            {
                await writer.UpdateAsync(threadId, path, value);

                // Publica STATE_DELTA no event bus para o SSE handler entregar ao frontend.
                // Constrói JSON Patch (RFC 6902) com operação "add" para o path atualizado.
                var jsonPatchPath = "/" + path.Replace(".", "/");
                var patchArray = JsonSerializer.Serialize(new[]
                {
                    new { op = "add", path = jsonPatchPath, value }
                });
                var deltaElement = JsonDocument.Parse(patchArray).RootElement.Clone();
                await eventBus.PublishAsync(execId, new WorkflowEventEnvelope
                {
                    EventType = "state_delta",
                    ExecutionId = execId,
                    Payload = JsonSerializer.Serialize(deltaElement)
                });
            };
        }

        // Expõe contexto de execução para code executors e TokenTrackingChatClient via um único AsyncLocal
        DelegateExecutor.Current.Value = new EfsAiHub.Core.Agents.Execution.ExecutionContext(
            ExecutionId: execution.ExecutionId,
            WorkflowId: execution.WorkflowId,
            Input: execution.Input,
            PromptVersions: new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
            NodeCallback: CreateNodeCallback(execution, nodeTracker),
            Budget: new EfsAiHub.Core.Agents.Execution.ExecutionBudget(maxTokensPerExecution, maxCostUsdPerExecution),
            UserId: guardUserId,
            GuardMode: guardMode,
            AgentVersions: new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
            UpdateSharedState: updateSharedState,
            ConversationId: conversationId,
            EnrichmentRules: enrichmentRules,
            Persona: persona);

        try
        {
            // Checkpoint manager vinculado ao PgCheckpointStore (via adapter) para
            // que o framework persista SuperStep snapshots durante HITL automaticamente.
            // Nenhum CheckpointAsync explícito é chamado no HandleHitlRequestAsync:
            // o próprio framework cria um checkpoint ao final de cada SuperStep.
            var checkpointManager = _checkpointAdapter.CreateManager();
            var inputMessages = ChatTurnContextMapper.Build(execution.Input, orchestrationMode);

            _logger.LogInformation("Execução '{ExecutionId}': chamando RunStreamingAsync.", execution.ExecutionId);
            await using var run = await InProcessExecution.RunStreamingAsync(
                (Workflow)workflow, inputMessages, checkpointManager);

            // Para os modos Handoff e GroupChat, o HandoffsStartExecutor/GroupChatCoordinator
            // acumula mensagens e aguarda um TurnToken antes de invocar os agentes.
            // emitEvents: true também habilita AgentResponseUpdateEvent (tokens de streaming).
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            // Input já foi consumido pelo workflow runtime — limpar para reduzir tamanho do blob
            execution.Input = null;

            _logger.LogInformation("Execução '{ExecutionId}': RunStreamingAsync retornou, iniciando WatchStreamAsync.", execution.ExecutionId);

            var outputParts = new List<string>();
            await ProcessEventLoopAsync(execution, run, orchestrationMode, maxAgentInvocations, nodeTracker, agentNames, outputParts, token);
            await FinalizeLastAgentAsync(execution, nodeTracker, outputParts, agentNames);
        }
        catch (OperationCanceledException)
        {
            var isTimeout = timeoutCts.IsCancellationRequested;
            _logger.LogWarning("Execução '{ExecutionId}' {Reason}.", execution.ExecutionId,
                isTimeout ? "timeout" : "cancelada");
            await _failureWriter.MarkCancelledAsync(execution, isTimeout);
        }
        catch (HitlRejectedException ex)
        {
            _logger.LogInformation("Execução '{ExecutionId}' rejeitada por HITL no nó '{NodeId}'.",
                execution.ExecutionId, ex.NodeId);
            await _failureWriter.MarkFailedAsync(execution, ex.Message, ErrorCategory.HitlRejected);
        }
        catch (Exception ex) when (UnwrapHitlRejection(ex) is { } hitlEx)
        {
            _logger.LogInformation("Execução '{ExecutionId}' rejeitada por HITL no nó '{NodeId}'.",
                execution.ExecutionId, hitlEx.NodeId);
            await _failureWriter.MarkFailedAsync(execution, hitlEx.Message, ErrorCategory.HitlRejected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execução '{ExecutionId}' falhou.", execution.ExecutionId);
            await _failureWriter.MarkFailedAsync(execution, ex.Message, ClassifyError(ex));
        }
        finally
        {
            await _tokenBatcher.RemoveAsync(execution.ExecutionId);

            // Persiste mapa de prompt versions usadas nesta execução
            var promptVersions = DelegateExecutor.Current.Value?.PromptVersions;
            if (promptVersions is { Count: > 0 } && execution.Metadata is not null)
                execution.Metadata["promptVersions"] = JsonSerializer.Serialize(promptVersions);

            // nodeTracker.DisposeAsync() (via await using) encerra spans de agentes órfãos

            DelegateExecutor.Current.Value = null;
            MetricsRegistry.ActiveExecutions.Add(-1, workflowTag);

            // Registra counter terminal e duração
            var durationMs = (DateTime.UtcNow - execution.StartedAt).TotalMilliseconds;
            MetricsRegistry.WorkflowDurationMs.Record(durationMs, workflowTag);

            switch (execution.Status)
            {
                case WorkflowStatus.Completed:
                    MetricsRegistry.WorkflowsCompleted.Add(1, workflowTag);
                    break;
                case WorkflowStatus.Failed:
                    MetricsRegistry.WorkflowsFailed.Add(1, workflowTag,
                        new KeyValuePair<string, object?>("error.category",
                            execution.ErrorCategory?.ToString() ?? "unknown"));
                    break;
                case WorkflowStatus.Cancelled:
                    MetricsRegistry.WorkflowsCancelled.Add(1, workflowTag);
                    break;
            }

            // Fix #A5/A: em estados terminais, libera o índice em memória do checkpoint adapter
            // e deleta o checkpoint persistido. Paused NÃO evita (precisa retomar depois).
            if (execution.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.Cancelled)
            {
                try { await _checkpointAdapter.EvictSessionAsync(execution.ExecutionId, deletePersistent: true, CancellationToken.None); }
                catch (Exception ex) { _logger.LogDebug(ex, "[WorkflowRunner] EvictSessionAsync falhou para '{ExecutionId}'.", execution.ExecutionId); }
            }
        }
    }

    /// <summary>
    /// Retoma uma execução previamente pausada por HITL a partir do checkpoint
    /// persistido. Reaproveita o mesmo pipeline do RunAsync (ProcessEventLoopAsync +
    /// FinalizeLastAgentAsync). Usado pelo HitlRecoveryService no startup.
    /// </summary>
    public async Task ResumeAsync(
        WorkflowExecution execution,
        object workflow,
        int timeoutSeconds,
        int maxAgentInvocations,
        int maxTokensPerExecution,
        decimal? maxCostUsdPerExecution,
        EfsAiHub.Core.Agents.Execution.AccountGuardMode guardMode,
        IReadOnlyDictionary<string, string>? agentNames,
        OrchestrationMode orchestrationMode,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        using var activity = ActivitySources.WorkflowExecutionSource.StartActivity("WorkflowResume");
        activity?.SetTag("workflow.id", execution.WorkflowId);
        activity?.SetTag("execution.id", execution.ExecutionId);

        execution.Status = WorkflowStatus.Running;
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);

        var workflowTag = new KeyValuePair<string, object?>("workflow.id", execution.WorkflowId);
        MetricsRegistry.ActiveExecutions.Add(1, workflowTag);

        await using var nodeTracker = new NodeStateTracker();

        string? guardUserId = null;
        string? guardUserType = null;
        if (!string.IsNullOrEmpty(execution.Input))
        {
            try
            {
                using var doc = JsonDocument.Parse(execution.Input);
                if (doc.RootElement.TryGetProperty("userId", out var uidEl))
                    guardUserId = uidEl.GetString();
                if (doc.RootElement.TryGetProperty("metadata", out var metaEl)
                    && metaEl.ValueKind == JsonValueKind.Object
                    && metaEl.TryGetProperty("userType", out var utEl))
                    guardUserType = utEl.GetString();
            }
            catch { }
        }

        // Resolve Persona também no resume — fallback silencioso via provider.
        EfsAiHub.Core.Abstractions.Identity.Persona.UserPersona? resumePersona = null;
        if (_personaProvider is not null && !string.IsNullOrWhiteSpace(guardUserId))
        {
            resumePersona = await _personaProvider.ResolveAsync(
                guardUserId, guardUserType ?? "cliente", ct);
        }

        // Extrai conversationId da metadata (Chat executions) para shared state
        string? resumeConversationId = null;
        execution.Metadata?.TryGetValue("conversationId", out resumeConversationId);

        Func<string, System.Text.Json.JsonElement, Task>? resumeUpdateState = null;
        if (_sharedStateWriter is not null && resumeConversationId is not null)
        {
            var writer = _sharedStateWriter;
            var threadId = resumeConversationId;
            var execId = execution.ExecutionId;
            var eventBus = _eventBus;
            resumeUpdateState = async (path, value) =>
            {
                await writer.UpdateAsync(threadId, path, value);

                var jsonPatchPath = "/" + path.Replace(".", "/");
                var patchArray = JsonSerializer.Serialize(new[]
                {
                    new { op = "add", path = jsonPatchPath, value }
                });
                var deltaElement = JsonDocument.Parse(patchArray).RootElement.Clone();
                await eventBus.PublishAsync(execId, new WorkflowEventEnvelope
                {
                    EventType = "state_delta",
                    ExecutionId = execId,
                    Payload = JsonSerializer.Serialize(deltaElement)
                });
            };
        }

        DelegateExecutor.Current.Value = new EfsAiHub.Core.Agents.Execution.ExecutionContext(
            ExecutionId: execution.ExecutionId,
            WorkflowId: execution.WorkflowId,
            Input: execution.Input,
            PromptVersions: new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
            NodeCallback: CreateNodeCallback(execution, nodeTracker),
            Budget: new EfsAiHub.Core.Agents.Execution.ExecutionBudget(maxTokensPerExecution, maxCostUsdPerExecution),
            UserId: guardUserId,
            GuardMode: guardMode,
            AgentVersions: new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
            UpdateSharedState: resumeUpdateState,
            ConversationId: resumeConversationId,
            EnrichmentRules: null,
            Persona: resumePersona);

        try
        {
            _logger.LogInformation("Execução '{ExecutionId}': retomando a partir de checkpoint.", execution.ExecutionId);
            var run = await _checkpointAdapter.TryResumeAsync((Workflow)workflow, execution.ExecutionId, token);
            if (run is null)
            {
                await _failureWriter.MarkFailedAsync(execution,
                    "Não foi possível retomar a execução a partir do checkpoint.",
                    ErrorCategory.CheckpointRecoveryFailed);
                return;
            }

            await using (run)
            {
                var outputParts = new List<string>();
                await ProcessEventLoopAsync(execution, run, orchestrationMode, maxAgentInvocations, nodeTracker, agentNames, outputParts, token);
                await FinalizeLastAgentAsync(execution, nodeTracker, outputParts, agentNames);
            }
        }
        catch (OperationCanceledException)
        {
            var isTimeout = timeoutCts.IsCancellationRequested;
            _logger.LogWarning("Resume '{ExecutionId}' {Reason}.", execution.ExecutionId,
                isTimeout ? "timeout" : "cancelado");
            await _failureWriter.MarkCancelledAsync(execution, isTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume '{ExecutionId}' falhou.", execution.ExecutionId);
            await _failureWriter.MarkFailedAsync(execution, ex.Message, ErrorCategory.CheckpointRecoveryFailed);
        }
        finally
        {
            await _tokenBatcher.RemoveAsync(execution.ExecutionId);

            var promptVersions = DelegateExecutor.Current.Value?.PromptVersions;
            if (promptVersions is { Count: > 0 } && execution.Metadata is not null)
                execution.Metadata["promptVersions"] = JsonSerializer.Serialize(promptVersions);

            DelegateExecutor.Current.Value = null;
            MetricsRegistry.ActiveExecutions.Add(-1, workflowTag);

            // Fix #A5/A: evict em estados terminais também no ResumeAsync.
            if (execution.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.Cancelled)
            {
                try { await _checkpointAdapter.EvictSessionAsync(execution.ExecutionId, deletePersistent: true, CancellationToken.None); }
                catch (Exception ex) { _logger.LogDebug(ex, "[WorkflowRunner] EvictSessionAsync (resume) falhou para '{ExecutionId}'.", execution.ExecutionId); }
            }
        }
    }

    private Action<string, bool, string> CreateNodeCallback(WorkflowExecution execution, NodeStateTracker nodeTracker)
    {
        return (nodeId, isCompleted, data) =>
        {
            if (!isCompleted)
            {
                var record = new NodeExecutionRecord
                {
                    NodeId = nodeId,
                    ExecutionId = execution.ExecutionId,
                    NodeType = "executor",
                    Status = "running",
                    StartedAt = DateTime.UtcNow
                };
                nodeTracker.SetRecord(nodeId, record);
                _nodePersistence.Enqueue(new NodePersistenceJob(
                    record,
                    execution.ExecutionId,
                    "node_started",
                    JsonSerializer.Serialize(new { nodeId, nodeType = "executor", timestamp = record.StartedAt })));
            }
            else
            {
                if (nodeTracker.TryGetRecord(nodeId, out var record))
                {
                    record.Status = "completed";
                    record.CompletedAt = DateTime.UtcNow;
                    record.Output = data;
                    _nodePersistence.Enqueue(new NodePersistenceJob(
                        record,
                        execution.ExecutionId,
                        "node_completed",
                        JsonSerializer.Serialize(new { nodeId, nodeType = "executor", output = data[..Math.Min(300, data.Length)], timestamp = record.CompletedAt })));
                }
            }
        };
    }

    // Conta invocações e detecta ping-pong em modos Sequential/Graph.
    // Concurrent e Handoff: apenas loga o agente ativo — sem limite nem ping-pong.
    private async Task ProcessEventLoopAsync(
        WorkflowExecution execution,
        StreamingRun run,
        OrchestrationMode orchestrationMode,
        int maxAgentInvocations,
        NodeStateTracker nodeTracker,
        IReadOnlyDictionary<string, string>? agentNames,
        List<string> outputParts,
        CancellationToken token)
    {
        var invocationCount = 0;
        var consecutivePingPongs = 0;
        string? previousAgentId = null;
        const int MaxConsecutivePingPongs = 3;

        await foreach (var evt in run.WatchStreamAsync().WithCancellation(token))
        {
            _logger.LogDebug("Execução '{ExecutionId}': evento {EventType}", execution.ExecutionId, evt.GetType().Name);

            if (evt is AgentResponseUpdateEvent tokenEvt2 &&
                tokenEvt2.ExecutorId is not null &&
                tokenEvt2.ExecutorId != nodeTracker.CurrentAgentId)
            {
                if (orchestrationMode is OrchestrationMode.Sequential
                                       or OrchestrationMode.Graph
                                       or OrchestrationMode.Handoff
                                       or OrchestrationMode.GroupChat)
                {
                    invocationCount++;

                    var currentOutput = nodeTracker.CurrentAgentId is not null
                        && nodeTracker.TryGetRecord(nodeTracker.CurrentAgentId, out var currentNode)
                        ? currentNode.Output : null;
                    var hasOutput = !string.IsNullOrWhiteSpace(currentOutput);

                    if (!hasOutput && tokenEvt2.ExecutorId == previousAgentId)
                    {
                        consecutivePingPongs++;
                        _logger.LogWarning(
                            "Execução '{ExecutionId}': ping-pong #{PingPong} detectado — '{From}' → '{To}' sem output.",
                            execution.ExecutionId, consecutivePingPongs, nodeTracker.CurrentAgentId ?? "(start)", tokenEvt2.ExecutorId);
                    }
                    else
                    {
                        consecutivePingPongs = 0;
                    }

                    previousAgentId = nodeTracker.CurrentAgentId;

                    _logger.LogDebug(
                        "Execução '{ExecutionId}': handoff #{Count} de '{From}' → '{To}'",
                        execution.ExecutionId, invocationCount, nodeTracker.CurrentAgentId ?? "(start)", tokenEvt2.ExecutorId);

                    if (consecutivePingPongs >= MaxConsecutivePingPongs)
                    {
                        _logger.LogWarning(
                            "Execução '{ExecutionId}': {Count} ping-pongs consecutivos sem output — abortando loop de handoff.",
                            execution.ExecutionId, consecutivePingPongs);
                        await _failureWriter.MarkFailedAsync(execution,
                            $"Loop de handoff detectado: {consecutivePingPongs} trocas consecutivas entre agentes sem produzir resposta. Possível incompatibilidade de prompts.",
                            ErrorCategory.AgentLoopLimit);
                        break;
                    }

                    if (invocationCount > maxAgentInvocations)
                    {
                        _logger.LogWarning(
                            "Execução '{ExecutionId}': limite de {Max} invocações de agente atingido — abortando para evitar loop.",
                            execution.ExecutionId, maxAgentInvocations);
                        await _failureWriter.MarkFailedAsync(execution,
                            $"Limite de {maxAgentInvocations} invocações de agente atingido. Possível loop de handoff.",
                            ErrorCategory.AgentLoopLimit);
                        break;
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Execução '{ExecutionId}': agente ativo '{To}' (modo {Mode})",
                        execution.ExecutionId, tokenEvt2.ExecutorId, orchestrationMode);
                }
            }

            await HandleEventAsync(execution, run, evt, outputParts, nodeTracker, agentNames, token);

            if (execution.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.Cancelled)
                break;
        }

        // When WithCancellation(token) exits the loop without throwing, check if cancellation
        // was requested. If so, propagate it so RunAsync.catch(OperationCanceledException) handles it.
        if (token.IsCancellationRequested &&
            execution.Status is not WorkflowStatus.Completed and not WorkflowStatus.Failed and not WorkflowStatus.Cancelled)
        {
            token.ThrowIfCancellationRequested();
        }

        _logger.LogInformation("Execução '{ExecutionId}': WatchStreamAsync concluído. Status={Status}",
            execution.ExecutionId, execution.Status);
    }

    private async Task FinalizeLastAgentAsync(
        WorkflowExecution execution,
        NodeStateTracker nodeTracker,
        List<string> outputParts,
        IReadOnlyDictionary<string, string>? agentNames)
    {
        var currentAgentId = nodeTracker.CurrentAgentId;

        if (currentAgentId is not null && nodeTracker.TryGetRecord(currentAgentId, out var lastAgent)
            && lastAgent.Status == "running")
        {
            lastAgent.Status = "completed";
            lastAgent.CompletedAt = DateTime.UtcNow;
            if (lastAgent.StartedAt.HasValue)
            {
                var agentDuration = (lastAgent.CompletedAt.Value - lastAgent.StartedAt.Value).TotalSeconds;
                MetricsRegistry.AgentInvocationDuration.Record(agentDuration,
                    new KeyValuePair<string, object?>("agent.id", currentAgentId),
                    new KeyValuePair<string, object?>("workflow.id", execution.WorkflowId));
            }
            nodeTracker.TryEndAgentSpan(currentAgentId, out _);
            nodeTracker.MaterializeOutput(currentAgentId);
            await _nodeRepo.SetNodeAsync(lastAgent);
            var lastAgentName = agentNames is not null && agentNames.TryGetValue(currentAgentId, out var lan) ? lan : null;
            await PublishEventAsync(execution.ExecutionId, "node_completed",
                new
                {
                    nodeId = currentAgentId,
                    nodeType = "agent",
                    agentId = currentAgentId,
                    agentName = lastAgentName,
                    timestamp = lastAgent.CompletedAt
                });
        }

        if (execution.Status == WorkflowStatus.Running)
        {
            // O workflow encerrou sem emitir WorkflowOutputEvent — provavelmente atingiu
            // o limite interno de SuperSteps do framework sem chegar ao nó final.
            nodeTracker.MaterializeAllOutputs();
            var partialOutput = outputParts.Count > 0
                ? string.Join("\n", outputParts)
                : string.Join("\n", nodeTracker.AllRecords
                    .Where(n => n.Output is not null)
                    .OrderBy(n => n.StartedAt)
                    .Select(n => n.Output));

            _logger.LogWarning("Execução '{ExecutionId}': encerrou sem WorkflowOutputEvent — marcada como Failed.",
                execution.ExecutionId);
            execution.Output = partialOutput;
            await _failureWriter.MarkFailedAsync(execution,
                "Workflow encerrou sem atingir o nó final. Possível limite de iterações do framework.",
                ErrorCategory.FrameworkError);
        }
    }

    private async Task HandleEventAsync(
        WorkflowExecution execution,
        StreamingRun run,
        WorkflowEvent evt,
        List<string> outputParts,
        NodeStateTracker nodeTracker,
        IReadOnlyDictionary<string, string>? agentNames,
        CancellationToken ct)
    {
        switch (evt)
        {
            case AgentResponseUpdateEvent tokenEvt:
                await _agentHandoffHandler.HandleAsync(tokenEvt, execution, nodeTracker, agentNames, ct);
                break;

            case WorkflowOutputEvent outputEvt:
                // Modo Handoff/GroupChat: Data é List<ChatMessage> — extrai o texto da última mensagem do assistente.
                // Modo Concurrent: Data é List<ChatMessage> com uma resposta por agente — coleta todas.
                // Modo Sequential/Graph: Data já é uma string.

                // Gravar último agente ativo antes de notificar (para otimização de entry point)
                if (nodeTracker.CurrentAgentId is not null)
                    execution.Metadata["lastActiveAgentId"] = nodeTracker.CurrentAgentId;

                string output;
                if (outputEvt.Data is IEnumerable<Microsoft.Extensions.AI.ChatMessage> outputMsgs)
                {
                    var assistantMessages = outputMsgs
                        .Where(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))
                        .ToList();

                    // Concurrent: combina todas as respostas dos agentes; outros modos: usa apenas a última mensagem do assistente
                    output = assistantMessages.Count > 1
                        ? string.Join("\n\n---\n\n", assistantMessages.Select(m => m.Text))
                        : assistantMessages.LastOrDefault()?.Text ?? string.Empty;

                    if (!string.IsNullOrEmpty(output))
                    {
                        outputParts.Add(output);
                        await _failureWriter.MarkCompletedAsync(execution, output, ct);
                    }
                }
                else
                {
                    output = outputEvt.Data?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(output))
                    {
                        outputParts.Add(output);
                        await _failureWriter.MarkCompletedAsync(execution, string.Join("\n", outputParts), ct);
                    }
                }
                break;

            case SuperStepCompletedEvent:
                _logger.LogDebug("Execução '{ExecutionId}': superstep completado.", execution.ExecutionId);
                // step_completed removido: payload vazio sem valor analítico (consolidação especialistas)
                break;

            case RequestInfoEvent requestEvt:
                await HandleHitlRequestAsync(execution, run, requestEvt, ct);
                break;

            case WorkflowErrorEvent errorEvt:
                // When ct is already cancelled, the framework converts OperationCanceledException
                // into a WorkflowErrorEvent. Route to MarkCancelledAsync to emit RUN_ERROR CANCELLED.
                if (ct.IsCancellationRequested)
                    await _failureWriter.MarkCancelledAsync(execution, isTimeout: false);
                else if (IsHitlRejectionError(errorEvt.Data?.ToString()))
                {
                    var msg = ExtractHitlMessage(errorEvt.Data?.ToString()!);
                    _logger.LogInformation("Execução '{ExecutionId}' rejeitada por HITL declarativo.",
                        execution.ExecutionId);
                    await _failureWriter.MarkFailedAsync(execution, msg, ErrorCategory.HitlRejected);
                }
                else
                    await _failureWriter.MarkFailedAsync(execution,
                        errorEvt.Data?.ToString() ?? "Erro do framework",
                        ErrorCategory.FrameworkError);
                break;

            default:
                _logger.LogDebug("Execução '{ExecutionId}': evento ignorado — tipo={EventType}",
                    execution.ExecutionId, evt.GetType().Name);
                break;
        }
    }

    private async Task HandleHitlRequestAsync(
        WorkflowExecution execution,
        StreamingRun run,
        RequestInfoEvent requestEvt,
        CancellationToken ct)
    {
        var interactionId = Guid.NewGuid().ToString();
        var interactionRequest = new HumanInteractionRequest
        {
            InteractionId = interactionId,
            ExecutionId = execution.ExecutionId,
            WorkflowId = execution.WorkflowId,
            Prompt = requestEvt.Data?.ToString() ?? "Aprovação necessária",
            Context = DelegateExecutor.Current.Value?.Input
        };

        execution.Status = WorkflowStatus.Paused;
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);

        await PublishEventAsync(execution.ExecutionId, "hitl_required", new
        {
            interactionId,
            prompt = interactionRequest.Prompt
        });

        _logger.LogInformation("Execução '{ExecutionId}' pausada aguardando interação '{InteractionId}'.",
            execution.ExecutionId, interactionId);

        var resolution = await _hitlService.RequestAsync(interactionRequest, ct);

        execution.Status = WorkflowStatus.Running;
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);

        await run.SendResponseAsync(requestEvt.Request.CreateResponse(resolution));
    }

    private static ErrorCategory ClassifyError(Exception ex)
    {
        if (ex is EfsAiHub.Platform.Guards.BudgetExceededException)
            return ErrorCategory.BudgetExceeded;
        // Classifica exceções por tipo para agregar métricas de erro
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return ErrorCategory.LlmRateLimit;
            return ErrorCategory.LlmError;
        }
        if (ex.Message.Contains("content filter", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
            return ErrorCategory.LlmContentFilter;
        if (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return ErrorCategory.LlmRateLimit;
        return ErrorCategory.Unknown;
    }

    /// <summary>
    /// Unwrap HitlRejectedException de TargetInvocationException (wrapping do framework).
    /// </summary>
    private static HitlRejectedException? UnwrapHitlRejection(Exception ex)
    {
        var inner = ex;
        while (inner is not null)
        {
            if (inner is HitlRejectedException hitl) return hitl;
            inner = inner.InnerException;
        }
        return null;
    }

    private static bool IsHitlRejectionError(string? errorData)
        => errorData?.Contains("HitlRejectedException", StringComparison.Ordinal) == true;

    private static string ExtractHitlMessage(string errorData)
    {
        // Extrai "HITL rejeitado no nó 'xxx': yyy" do stack trace
        const string marker = "HitlRejectedException: ";
        var idx = errorData.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return "HITL rejeitado";
        var start = idx + marker.Length;
        var end = errorData.IndexOf('\n', start);
        return end > start ? errorData[start..end].Trim() : errorData[start..].Trim();
    }

    private async Task PublishEventAsync(string executionId, string eventType, object payload)
    {
        // Flush tokens acumulados antes de publicar evento de controle (garante ordem correta no SSE)
        await _tokenBatcher.FlushAsync(executionId);

        var envelope = new WorkflowEventEnvelope
        {
            EventType = eventType,
            ExecutionId = executionId,
            Payload = JsonSerializer.Serialize(payload)
        };
        // Publica em tempo real (SSE) via PostgreSQL NOTIFY (SSE backbone)
        await _eventBus.PublishAsync(executionId, envelope);
    }
}

/// <summary>
/// Wrapper mutável simples para passar uma string por referência em métodos assíncronos.
/// </summary>
internal sealed class StringHolder
{
    public string? Value { get; set; }
}
