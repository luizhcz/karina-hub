using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Contexto passado a cada <see cref="IEdgeHandler"/> com os recursos compartilhados
/// necessários para resolver nós e logar diagnósticos.
/// </summary>
public sealed class EdgeBuildContext
{
    private readonly ILogger _logger;
    private readonly IReadOnlyDictionary<string, ExecutorBinding> _bindingMap;

    public EdgeBuildContext(
        IReadOnlyDictionary<string, ExecutorBinding> bindingMap,
        string workflowId,
        ILogger logger)
    {
        _bindingMap = bindingMap;
        WorkflowId = workflowId;
        _logger = logger;
    }

    public string WorkflowId { get; }
    public ILogger Logger => _logger;

    public bool TryResolve(string? nodeId, out ExecutorBinding binding)
    {
        if (nodeId is not null && _bindingMap.TryGetValue(nodeId, out binding!))
            return true;

        _logger.LogWarning(
            "Workflow '{WorkflowId}': nó '{NodeId}' não encontrado no bindingMap — aresta ignorada.",
            WorkflowId, nodeId);
        binding = default!;
        return false;
    }

    public ExecutorBinding? ResolveOrDefault(string nodeId)
    {
        if (_bindingMap.TryGetValue(nodeId, out var binding))
            return binding;

        _logger.LogWarning(
            "Workflow '{WorkflowId}': nó '{NodeId}' não encontrado — ignorado.",
            WorkflowId, nodeId);
        return null;
    }
}

/// <summary>
/// Estratégia para materializar uma aresta específica sobre o <see cref="WorkflowBuilder"/>.
/// Cada implementação trata um único <see cref="WorkflowEdgeType"/>, permitindo extensão
/// por composição (OCP) ao invés de modificação do switch central.
/// </summary>
public interface IEdgeHandler
{
    WorkflowEdgeType Type { get; }
    WorkflowBuilder Apply(WorkflowBuilder builder, WorkflowEdge edge, EdgeBuildContext ctx);
}

internal sealed class DirectEdgeHandler : IEdgeHandler
{
    public WorkflowEdgeType Type => WorkflowEdgeType.Direct;

    public WorkflowBuilder Apply(WorkflowBuilder builder, WorkflowEdge edge, EdgeBuildContext ctx)
    {
        if (!ctx.TryResolve(edge.From, out var from) || !ctx.TryResolve(edge.To, out var to))
            return builder;
        return builder.AddEdge<string>(from, to, _ => true);
    }
}

internal sealed class ConditionalEdgeHandler : IEdgeHandler
{
    public WorkflowEdgeType Type => WorkflowEdgeType.Conditional;
    private readonly IEdgePredicateEvaluator _evaluator;

    public ConditionalEdgeHandler(IEdgePredicateEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public WorkflowBuilder Apply(WorkflowBuilder builder, WorkflowEdge edge, EdgeBuildContext ctx)
    {
        if (!ctx.TryResolve(edge.From, out var from) || !ctx.TryResolve(edge.To, out var to))
            return builder;

        if (edge.Predicate is null)
        {
            // EnsureInvariants no save bloqueia esse caminho — chegar aqui é bug ou
            // workflow corrompido carregado do banco. Tratamos como Direct pra manter
            // execução determinística.
            ctx.Logger.LogError(
                "Workflow '{WorkflowId}': Conditional edge {From}→{To} sem Predicate — comportamento indefinido, caindo em Direct.",
                ctx.WorkflowId, edge.From, edge.To);
            return builder.AddEdge(from, to);
        }

        var predicate = edge.Predicate;
        return builder.AddEdge<string>(from, to, output => _evaluator.Evaluate(predicate, output));
    }
}

internal sealed class SwitchEdgeHandler : IEdgeHandler
{
    public WorkflowEdgeType Type => WorkflowEdgeType.Switch;
    private readonly IEdgePredicateEvaluator _evaluator;

    public SwitchEdgeHandler(IEdgePredicateEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public WorkflowBuilder Apply(WorkflowBuilder builder, WorkflowEdge edge, EdgeBuildContext ctx)
    {
        if (!ctx.TryResolve(edge.From, out var from)) return builder;

        if (edge.Cases.Count == 0)
        {
            ctx.Logger.LogWarning(
                "Workflow '{WorkflowId}': aresta Switch de '{From}' sem Cases — ignorada.",
                ctx.WorkflowId, edge.From);
            return builder;
        }

        return builder.AddSwitch(from, switchBuilder =>
        {
            foreach (var @case in edge.Cases)
            {
                var targets = @case.Targets
                    .Select(ctx.ResolveOrDefault)
                    .Where(b => b is not null)
                    .Select(b => b!)
                    .ToList();

                if (targets.Count == 0)
                {
                    ctx.Logger.LogWarning(
                        "Workflow '{WorkflowId}': Switch case sem targets válidos — ignorado.", ctx.WorkflowId);
                    continue;
                }

                if (@case.IsDefault || @case.Predicate is null)
                {
                    switchBuilder.WithDefault(targets);
                }
                else
                {
                    var casePredicate = @case.Predicate;
                    switchBuilder.AddCase<string>(
                        output => _evaluator.Evaluate(casePredicate, output),
                        targets);
                }
            }
        });
    }
}

internal sealed class FanOutEdgeHandler : IEdgeHandler
{
    public WorkflowEdgeType Type => WorkflowEdgeType.FanOut;

    public WorkflowBuilder Apply(WorkflowBuilder builder, WorkflowEdge edge, EdgeBuildContext ctx)
    {
        if (!ctx.TryResolve(edge.From, out var from)) return builder;

        var targetIds = edge.Targets.Count > 0 ? edge.Targets
                      : edge.To is not null ? [edge.To]
                      : [];

        if (targetIds.Count == 0)
        {
            ctx.Logger.LogWarning(
                "Workflow '{WorkflowId}': aresta FanOut de '{From}' sem Targets — ignorada.",
                ctx.WorkflowId, edge.From);
            return builder;
        }

        var targets = targetIds
            .Select(ctx.ResolveOrDefault)
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        return builder.AddFanOutEdge(from, targets);
    }
}

internal sealed class FanInEdgeHandler : IEdgeHandler
{
    public WorkflowEdgeType Type => WorkflowEdgeType.FanIn;

    public WorkflowBuilder Apply(WorkflowBuilder builder, WorkflowEdge edge, EdgeBuildContext ctx)
    {
        if (!ctx.TryResolve(edge.To, out var target)) return builder;

        var sourceIds = edge.Sources.Count > 0 ? edge.Sources
                      : edge.From is not null ? [edge.From]
                      : [];

        if (sourceIds.Count == 0)
        {
            ctx.Logger.LogWarning(
                "Workflow '{WorkflowId}': aresta FanIn para '{To}' sem Sources — ignorada.",
                ctx.WorkflowId, edge.To);
            return builder;
        }

        var sources = sourceIds
            .Select(ctx.ResolveOrDefault)
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        return builder.AddFanInBarrierEdge(sources, target);
    }
}
