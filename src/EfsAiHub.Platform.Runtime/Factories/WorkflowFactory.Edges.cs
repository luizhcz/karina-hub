using EfsAiHub.Core.Orchestration.Enums;
using Microsoft.Agents.AI.Workflows;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Parte da <see cref="WorkflowFactory"/> responsável por despachar arestas para
/// os handlers registrados. A lógica concreta de cada tipo vive em
/// <see cref="EdgeHandlers"/>, permitindo extensão (OCP) sem tocar neste arquivo.
/// </summary>
public partial class WorkflowFactory
{
    private static readonly IReadOnlyDictionary<WorkflowEdgeType, IEdgeHandler> EdgeHandlers =
        new IEdgeHandler[]
        {
            new DirectEdgeHandler(),
            new ConditionalEdgeHandler(),
            new SwitchEdgeHandler(),
            new FanOutEdgeHandler(),
            new FanInEdgeHandler()
        }.ToDictionary(h => h.Type);

    private WorkflowBuilder AddEdge(
        WorkflowBuilder builder,
        WorkflowEdge edge,
        IReadOnlyDictionary<string, ExecutorBinding> bindingMap,
        string workflowId)
    {
        if (!EdgeHandlers.TryGetValue(edge.EdgeType, out var handler))
            throw new NotSupportedException($"EdgeType '{edge.EdgeType}' não suportado.");

        var ctx = new EdgeBuildContext(bindingMap, workflowId, _logger);
        return handler.Apply(builder, edge, ctx);
    }
}
