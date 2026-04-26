using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Parte da <see cref="WorkflowFactory"/> responsável por despachar arestas para
/// os handlers registrados. A lógica concreta de cada tipo vive em
/// <see cref="EdgeHandlers"/>, permitindo extensão (OCP) sem tocar neste arquivo.
/// </summary>
public partial class WorkflowFactory
{
    /// <summary>
    /// Mapa instance-bound (não static) — Conditional/Switch handlers dependem do
    /// IEdgePredicateEvaluator injetado via DI no ctor da WorkflowFactory.
    /// </summary>
    private IReadOnlyDictionary<WorkflowEdgeType, IEdgeHandler>? _edgeHandlers;

    private IReadOnlyDictionary<WorkflowEdgeType, IEdgeHandler> EdgeHandlersMap =>
        _edgeHandlers ??= new IEdgeHandler[]
        {
            new DirectEdgeHandler(),
            new ConditionalEdgeHandler(_predicateEvaluator),
            new SwitchEdgeHandler(_predicateEvaluator),
            new FanOutEdgeHandler(),
            new FanInEdgeHandler()
        }.ToDictionary(h => h.Type);

    private WorkflowBuilder AddEdge(
        WorkflowBuilder builder,
        WorkflowEdge edge,
        IReadOnlyDictionary<string, ExecutorBinding> bindingMap,
        string workflowId)
    {
        if (!EdgeHandlersMap.TryGetValue(edge.EdgeType, out var handler))
            throw new NotSupportedException($"EdgeType '{edge.EdgeType}' não suportado.");

        var ctx = new EdgeBuildContext(bindingMap, workflowId, _logger);
        return handler.Apply(builder, edge, ctx);
    }
}
