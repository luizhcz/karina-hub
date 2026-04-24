using System.Text.RegularExpressions;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Platform.Runtime.Hitl;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Constrói um workflow executável a partir de uma WorkflowDefinition.
/// Mapeia OrchestrationMode para o builder correto do Microsoft Agent Framework.
///
/// Modos suportados:
///   Sequential  → AgentWorkflowBuilder.BuildSequential
///   Concurrent  → AgentWorkflowBuilder.BuildConcurrent
///   Handoff     → AgentWorkflowBuilder.CreateHandoffBuilderWith  (suporta grafo dirigido via Edges)
///   GroupChat   → AgentWorkflowBuilder.CreateGroupChatBuilderWith
///   Graph       → WorkflowBuilder (baixo nível) — suporta Direct, Conditional, Switch, FanOut, FanIn
///                 e permite misturar AIAgents com DelegateExecutors (code-only steps)
/// </summary>
public partial class WorkflowFactory : IWorkflowFactory
{
    private readonly IAgentFactory _agentFactory;
    private readonly IFunctionToolRegistry _functionRegistry;
    private readonly ICodeExecutorRegistry _executorRegistry;
    private readonly IHumanInteractionService _hitlService;
    private readonly IWorkflowEventBus _eventBus;
    private readonly ILogger<WorkflowFactory> _logger;

    public WorkflowFactory(
        IAgentFactory agentFactory,
        IFunctionToolRegistry functionRegistry,
        ICodeExecutorRegistry executorRegistry,
        IHumanInteractionService hitlService,
        IWorkflowEventBus eventBus,
        ILogger<WorkflowFactory> logger)
    {
        _agentFactory = agentFactory;
        _functionRegistry = functionRegistry;
        _executorRegistry = executorRegistry;
        _hitlService = hitlService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<ExecutableWorkflow> BuildWorkflowAsync(WorkflowDefinition definition, string? startAgentId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Construindo workflow '{WorkflowId}' no modo {Mode}, startAgent='{StartAgent}'",
            definition.Id, definition.OrchestrationMode, startAgentId ?? "(default)");

        // Graph mode usa DelegateExecutors com IChatClient direto (evita incompatibilidade de tipos)
        if (definition.OrchestrationMode == OrchestrationMode.Graph)
        {
            var graphWorkflow = await BuildGraphAsync(definition, ct);
            if (definition.Configuration.ExposeAsAgent)
            {
                _logger.LogInformation("Expondo workflow '{WorkflowId}' como AIAgent.", definition.Id);
                return ExecutableWorkflow.FromAgent(graphWorkflow.AsAIAgent(
                    id: definition.Id, name: definition.Name,
                    description: definition.Configuration.ExposedAgentDescription));
            }
            return ExecutableWorkflow.FromWorkflow(graphWorkflow);
        }

        var agentMap = await _agentFactory.CreateAgentsForWorkflowAsync(definition, ct);

        // Monta lista ordenada respeitando a ordem dos agentes no JSON
        var agents = definition.Agents
            .Select(r => (AIAgent)agentMap[r.AgentId].Value)
            .ToList();

        Workflow workflow = definition.OrchestrationMode switch
        {
            OrchestrationMode.Sequential => BuildSequential(agents),
            OrchestrationMode.Concurrent => BuildConcurrent(agents),
            OrchestrationMode.Handoff => BuildHandoff(definition, agentMap, startAgentId),
            OrchestrationMode.GroupChat => BuildGroupChat(definition, agentMap),
            _ => throw new NotSupportedException($"OrchestrationMode '{definition.OrchestrationMode}' não suportado.")
        };

        if (definition.Configuration.ExposeAsAgent)
        {
            _logger.LogInformation("Expondo workflow '{WorkflowId}' como AIAgent.", definition.Id);
            return ExecutableWorkflow.FromAgent(workflow.AsAIAgent(
                id: definition.Id,
                name: definition.Name,
                description: definition.Configuration.ExposedAgentDescription));
        }

        return ExecutableWorkflow.FromWorkflow(workflow);
    }

    private static Workflow BuildSequential(List<AIAgent> agents)
        => AgentWorkflowBuilder.BuildSequential(agents);

    private static Workflow BuildConcurrent(List<AIAgent> agents)
        => AgentWorkflowBuilder.BuildConcurrent(agents);

    private Workflow BuildHandoff(WorkflowDefinition definition, IReadOnlyDictionary<string, ExecutableWorkflow> agentMap, string? startAgentId)
    {
        // Pré-computar mapa sanitized→original uma única vez (O(n)).
        // O framework aplica GetDescriptiveId() que troca [^0-9A-Za-z_]+ por "_",
        // então precisamos mapear de volta para o ID original da definição.
        var sanitizedToOriginal = agentMap.Keys.ToDictionary(
            k => Regex.Replace(k, @"[^0-9A-Za-z_]+", "_"),
            k => k,
            StringComparer.OrdinalIgnoreCase);

        var entryAgentId = definition.Agents[0].AgentId;
        if (!string.IsNullOrEmpty(startAgentId))
        {
            var resolved = ResolveAgentId(startAgentId, agentMap, sanitizedToOriginal);
            if (resolved is not null)
            {
                entryAgentId = resolved;
                _logger.LogInformation("Handoff: usando '{StartAgent}' como entry point (otimização de continuação).", resolved);
            }
            else
            {
                _logger.LogWarning("Handoff: startAgentId '{StartAgent}' não encontrado — usando default.", startAgentId);
            }
        }

        var entryAgent = (AIAgent)agentMap[entryAgentId].Value;
        var builder = AgentWorkflowBuilder.CreateHandoffBuilderWith(entryAgent);

        if (definition.Edges.Count > 0)
        {
            // Grafo dirigido explícito: cada WorkflowEdge mapeia para um handoff com reason/condition.
            // Suporta loops (ex: revisor → escritor se reprovado).
            foreach (var edge in definition.Edges)
            {
                if (!agentMap.TryGetValue(edge.From!, out var fromEw) ||
                    !agentMap.TryGetValue(edge.To!, out var toEw))
                {
                    _logger.LogWarning("Edge inválida: agente '{From}' ou '{To}' não encontrado — ignorada.",
                        edge.From, edge.To);
                    continue;
                }

                builder.WithHandoff((AIAgent)fromEw.Value, (AIAgent)toEw.Value, edge.Condition ?? string.Empty);
            }
        }
        else
        {
            // Topologia estrela completa: o manager (primeiro agente da definição) é o hub central,
            // conectado bidireccionalmente a todos os especialistas.
            // Quando startAgentId muda o entry point (ex: continuação de conversa),
            // precisamos garantir que o manager mantenha handoffs para TODOS os especialistas,
            // caso contrário agentes ficam isolados e causam loops de handoff.
            var managerId = definition.Agents[0].AgentId;
            var managerAgent = (AIAgent)agentMap[managerId].Value;

            var specialistAgents = definition.Agents
                .Where(r => r.AgentId != managerId)
                .Select(r => (AIAgent)agentMap[r.AgentId].Value)
                .ToArray();

            // Manager ↔ cada especialista (bidirecional)
            foreach (var specialist in specialistAgents)
                builder.WithHandoffs(managerAgent, [specialist]);

            if (specialistAgents.Length > 0)
                builder.WithHandoffs(specialistAgents, managerAgent);
        }

        return builder.Build();
    }

    private Workflow BuildGroupChat(WorkflowDefinition definition, IReadOnlyDictionary<string, ExecutableWorkflow> agentMap)
    {
        var managerRef = definition.Agents.FirstOrDefault(a =>
            string.Equals(a.Role, "manager", StringComparison.OrdinalIgnoreCase));

        var participantRefs = definition.Agents.Where(a =>
            !string.Equals(a.Role, "manager", StringComparison.OrdinalIgnoreCase)).ToList();

        var participants = participantRefs
            .Select(r => (AIAgent)agentMap[r.AgentId].Value)
            .ToArray();

        int maxRounds = definition.Configuration.MaxRounds ?? 5;

        if (managerRef is not null)
        {
            return AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(agts => new RoundRobinGroupChatManager(agts)
                {
                    MaximumIterationCount = maxRounds
                })
                .AddParticipants(participants)
                .Build();
        }

        // Sem manager explícito: round-robin com todos os agentes
        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agts => new RoundRobinGroupChatManager(agts)
            {
                MaximumIterationCount = maxRounds
            })
            .AddParticipants(participants)
            .Build();
    }

    /// <summary>
    /// Constrói um Workflow usando o WorkflowBuilder de baixo nível.
    /// Todos os nós são Executor&lt;string,string&gt; para garantir compatibilidade de tipos.
    /// Agentes LLM são encapsulados como DelegateExecutors que chamam IChatClient diretamente.
    /// </summary>
    private async Task<Workflow> BuildGraphAsync(WorkflowDefinition definition, CancellationToken ct)
    {
        var bindingMap = await BuildBindingMapAsync(definition, ct);

        if (bindingMap.Count == 0)
            throw new InvalidOperationException(
                $"Workflow '{definition.Id}' (Graph mode) não possui nenhum nó válido.");

        // Injeta bridge nodes automáticos para edges com InputSource.
        var processedEdges = InjectInputSourceBridges(definition.Edges, bindingMap, definition.Id);

        var (startId, endNodeIds) = ResolveGraphLayout(definition, processedEdges, bindingMap.Keys);

        // InProcessExecution.RunStreamingAsync entrega List<ChatMessage> como primeiro input.
        // DelegateExecutor<string,string> e AIAgent só aceitam string — sem este adapter,
        // o framework não consegue rotear o trigger e o workflow completa vazio.
        var chatTrigger = new ChatTriggerExecutor();
        ExecutorBinding triggerBinding = chatTrigger;

        var wfBuilder = new WorkflowBuilder(triggerBinding).WithName(definition.Name);

        if (!string.IsNullOrWhiteSpace(definition.Description))
            wfBuilder = wfBuilder.WithDescription(definition.Description);

        foreach (var (_, binding) in bindingMap)
            wfBuilder = wfBuilder.BindExecutor(binding);

        // Aresta automática: trigger → primeiro nó real
        wfBuilder = wfBuilder.AddEdge<string>(triggerBinding, bindingMap[startId], _ => true);

        foreach (var edge in processedEdges)
            wfBuilder = AddEdge(wfBuilder, edge, bindingMap, definition.Id);

        // Designar nós finais como fontes de output para que WorkflowOutputEvent seja emitido
        var endBindings = endNodeIds
            .Where(bindingMap.ContainsKey)
            .Select(id => bindingMap[id])
            .ToArray();

        _logger.LogInformation(
            "Workflow '{WorkflowId}': startId={StartId}, endNodes=[{EndNodes}], allNodes=[{AllNodes}]",
            definition.Id, startId, string.Join(", ", endNodeIds), string.Join(", ", bindingMap.Keys));

        if (endBindings.Length > 0)
            wfBuilder = wfBuilder.WithOutputFrom(endBindings);

        return wfBuilder.Build();
    }

    /// <summary>
    /// Cria o mapa de ExecutorBindings para agentes LLM e code executors.
    /// </summary>
    private async Task<Dictionary<string, ExecutorBinding>> BuildBindingMapAsync(
        WorkflowDefinition definition, CancellationToken ct)
    {
        var bindingMap = new Dictionary<string, ExecutorBinding>(StringComparer.OrdinalIgnoreCase);

        // Agentes LLM → DelegateExecutor via IChatClient (evita incompatibilidade de tipos com AIAgent)
        var hitlEnabled = definition.Configuration.EnableHumanInTheLoop;

        foreach (var agentRef in definition.Agents)
        {
            try
            {
                var handler = await _agentFactory.CreateLlmHandlerAsync(agentRef.AgentId, ct);
                Executor<string, string> executor = new DelegateExecutor(agentRef.AgentId, handler);

                if (hitlEnabled && agentRef.Hitl is not null)
                    executor = new HitlDecoratorExecutor(executor, agentRef.Hitl, _hitlService, _eventBus);

                bindingMap[agentRef.AgentId] = executor;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agente '{AgentId}' não pôde ser criado — ignorado.", agentRef.AgentId);
            }
        }

        // Code executors → DelegateExecutor via ICodeExecutorRegistry
        foreach (var step in definition.Executors)
        {
            if (!_executorRegistry.Contains(step.FunctionName))
            {
                _logger.LogWarning(
                    "Executor '{FunctionName}' não registrado em ICodeExecutorRegistry — passo '{StepId}' ignorado.",
                    step.FunctionName, step.Id);
                continue;
            }

            Executor<string, string> executor = _executorRegistry.CreateExecutor(step.Id, step.FunctionName);

            if (hitlEnabled && step.Hitl is not null)
                executor = new HitlDecoratorExecutor(executor, step.Hitl, _hitlService, _eventBus);

            bindingMap[step.Id] = executor;
        }

        return bindingMap;
    }

    /// <summary>
    /// Determina o nó inicial e os nós finais a partir das arestas.
    /// </summary>
    private (string startId, List<string> endNodeIds) ResolveGraphLayout(
        WorkflowDefinition definition, List<WorkflowEdge> edges, IEnumerable<string> nodeIds)
    {
        var allNodeIds = new HashSet<string>(nodeIds, StringComparer.OrdinalIgnoreCase);
        var targetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            CollectTargetIds(edge, targetIds);
            if (edge.From is not null) sourceIds.Add(edge.From);
        }

        // Nó inicial = o que não é destino de nenhuma aresta
        var startId = allNodeIds.FirstOrDefault(id => !targetIds.Contains(id))
                   ?? definition.Agents.FirstOrDefault()?.AgentId
                   ?? definition.Executors.FirstOrDefault()?.Id
                   ?? allNodeIds.First();

        // Nós finais: OutputNodes explícito (necessário em feedback loops) ou auto-detectado
        List<string> endNodeIds;
        if (definition.Configuration.OutputNodes is { Count: > 0 } explicitOutputs)
        {
            endNodeIds = explicitOutputs.Where(allNodeIds.Contains).ToList();
            if (endNodeIds.Count == 0)
                _logger.LogWarning(
                    "Workflow '{WorkflowId}': OutputNodes [{Nodes}] não correspondem a nenhum nó válido — usando auto-detecção.",
                    definition.Id, string.Join(", ", explicitOutputs));
        }
        else
        {
            endNodeIds = [];
        }

        if (endNodeIds.Count == 0)
        {
            endNodeIds = allNodeIds.Where(id => !sourceIds.Contains(id)).ToList();
            // Fallback: loop puro onde todos os nós têm arestas de saída
            if (endNodeIds.Count == 0)
                endNodeIds.Add(startId);
        }

        return (startId, endNodeIds);
    }

    /// <summary>
    /// Resolve um ExecutorId (possivelmente sanitizado pelo framework) para o ID original
    /// no agentMap usando o mapa pré-computado — O(1) por chamada.
    /// </summary>
    private static string? ResolveAgentId(
        string executorId,
        IReadOnlyDictionary<string, ExecutableWorkflow> agentMap,
        IReadOnlyDictionary<string, string> sanitizedToOriginal)
    {
        if (agentMap.ContainsKey(executorId))
            return executorId;

        return sanitizedToOriginal.TryGetValue(executorId, out var original) ? original : null;
    }

    /// <summary>
    /// Coleta todos os IDs que são destino de uma aresta (para cálculo do nó inicial).
    /// </summary>
    private static void CollectTargetIds(WorkflowEdge edge, HashSet<string> targets)
    {
        if (edge.To is not null)
            targets.Add(edge.To);

        foreach (var t in edge.Targets)
            targets.Add(t);

        foreach (var @case in edge.Cases)
            foreach (var t in @case.Targets)
                targets.Add(t);
    }

    /// <summary>
    /// Para cada edge com InputSource="WorkflowInput", injeta um nó bridge automático
    /// entre source e target(s). Retorna lista de edges processadas.
    /// Os bridge nodes são adicionados ao bindingMap para que sejam registrados no builder.
    /// </summary>
    private List<WorkflowEdge> InjectInputSourceBridges(
        IReadOnlyList<WorkflowEdge> edges,
        Dictionary<string, ExecutorBinding> bindingMap,
        string workflowId)
    {
        var result = new List<WorkflowEdge>(edges.Count);

        foreach (var edge in edges)
        {
            if (!string.Equals(edge.InputSource, "WorkflowInput", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(edge);
                continue;
            }

            // Coletar todos os target IDs desta edge
            var targetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectTargetIds(edge, targetIds);

            if (targetIds.Count == 0)
            {
                result.Add(edge);
                continue;
            }

            // Criar bridge node para cada target
            foreach (var targetId in targetIds)
            {
                var bridgeId = $"__bridge_{targetId}__";
                if (!bindingMap.ContainsKey(bridgeId))
                {
                    var bridge = new InputSourceBridgeExecutor(targetId);
                    bindingMap[bridgeId] = bridge;
                }
            }

            // Reescrever edges: source → bridge (mantém tipo/condição) + bridge → target (Direct)
            result.AddRange(RewriteEdgeWithBridges(edge, targetIds));

            _logger.LogInformation(
                "Workflow '{WorkflowId}': InputSource bridges injetados para edge {From} → [{Targets}]",
                workflowId, edge.From, string.Join(", ", targetIds));
        }

        return result;
    }

    /// <summary>
    /// Reescreve uma edge substituindo targets originais por bridge nodes,
    /// e adiciona edges Direct de cada bridge ao target original.
    /// </summary>
    private static List<WorkflowEdge> RewriteEdgeWithBridges(
        WorkflowEdge edge,
        HashSet<string> targetIds)
    {
        var result = new List<WorkflowEdge>();
        var bridgeId = (string targetId) => $"__bridge_{targetId}__";

        switch (edge.EdgeType)
        {
            case WorkflowEdgeType.Direct:
            {
                // A → B  ⟹  A → __bridge_B__ (Direct) + __bridge_B__ → B (Direct)
                var bid = bridgeId(edge.To!);
                result.Add(new WorkflowEdge { From = edge.From, To = bid, EdgeType = WorkflowEdgeType.Direct });
                result.Add(new WorkflowEdge { From = bid, To = edge.To, EdgeType = WorkflowEdgeType.Direct });
                break;
            }

            case WorkflowEdgeType.Conditional:
            {
                // A → B (condition)  ⟹  A → __bridge_B__ (Conditional, mesma condition) + __bridge_B__ → B (Direct)
                var bid = bridgeId(edge.To!);
                result.Add(new WorkflowEdge { From = edge.From, To = bid, EdgeType = WorkflowEdgeType.Conditional, Condition = edge.Condition });
                result.Add(new WorkflowEdge { From = bid, To = edge.To, EdgeType = WorkflowEdgeType.Direct });
                break;
            }

            case WorkflowEdgeType.Switch:
            {
                // Switch: substituir targets nos cases por bridges + adicionar bridge → target (Direct)
                var rewrittenCases = edge.Cases.Select(c => new WorkflowSwitchCase
                {
                    Condition = c.Condition,
                    IsDefault = c.IsDefault,
                    Targets = c.Targets.Select(t => targetIds.Contains(t) ? bridgeId(t) : t).ToList()
                }).ToList();

                result.Add(new WorkflowEdge
                {
                    From = edge.From,
                    EdgeType = WorkflowEdgeType.Switch,
                    Cases = rewrittenCases
                });

                // Edges bridge → target (Direct) para cada target substituído
                foreach (var tid in targetIds)
                    result.Add(new WorkflowEdge { From = bridgeId(tid), To = tid, EdgeType = WorkflowEdgeType.Direct });

                break;
            }

            case WorkflowEdgeType.FanOut:
            {
                // FanOut: substituir targets por bridges + adicionar bridge → target (Direct)
                var rewrittenTargets = (edge.Targets.Count > 0 ? edge.Targets : edge.To is not null ? [edge.To] : [])
                    .Select(t => targetIds.Contains(t) ? bridgeId(t) : t)
                    .ToList();

                result.Add(new WorkflowEdge { From = edge.From, EdgeType = WorkflowEdgeType.FanOut, Targets = rewrittenTargets });

                foreach (var tid in targetIds)
                    result.Add(new WorkflowEdge { From = bridgeId(tid), To = tid, EdgeType = WorkflowEdgeType.Direct });

                break;
            }

            default:
                // FanIn e outros: manter como está (InputSource não faz sentido em FanIn)
                result.Add(edge);
                break;
        }

        return result;
    }
}
