using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Valida as arestas de um workflow (Handoff e Graph). Extraído de WorkflowService
/// para reduzir complexidade e permitir teste isolado das regras de edges.
///
/// Regras topológicas e contratuais — a validação tipada de predicates
/// (<see cref="EdgePredicate.Path"/>, operadores vs tipo, schema do produtor existente)
/// fica em <c>EnsureInvariants</c> com envelope de erro estruturado por <c>error_code</c>.
/// </summary>
public static class EdgeValidator
{
    public static void Validate(
        WorkflowDefinition definition,
        HashSet<string> agentIdSet,
        List<string> errors)
    {
        var mode = definition.OrchestrationMode;

        if (definition.Edges.Count > 0 && mode != OrchestrationMode.Graph && mode != OrchestrationMode.Handoff)
            errors.Add($"Campo 'edges' é válido apenas nos modos 'Graph' e 'Handoff' (modo atual: '{mode}').");

        if (mode == OrchestrationMode.Handoff && definition.Edges.Count > 0)
        {
            ValidateHandoffEdges(definition, agentIdSet, errors);
            return;
        }

        if (mode != OrchestrationMode.Graph || definition.Edges.Count == 0) return;

        ValidateGraphEdges(definition, errors);
    }

    private static void ValidateHandoffEdges(
        WorkflowDefinition definition,
        HashSet<string> agentIdSet,
        List<string> errors)
    {
        foreach (var edge in definition.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.From) || !agentIdSet.Contains(edge.From))
                errors.Add($"Edge Handoff: 'from' = '{edge.From}' não encontrado nos agents do workflow.");
            if (string.IsNullOrWhiteSpace(edge.To) || !agentIdSet.Contains(edge.To))
                errors.Add($"Edge Handoff: 'to' = '{edge.To}' não encontrado nos agents do workflow.");
        }
    }

    private static void ValidateGraphEdges(WorkflowDefinition definition, List<string> errors)
    {
        var knownNodes = new HashSet<string>(
            definition.Agents.Select(a => a.AgentId).Concat(definition.Executors.Select(e => e.Id)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var edge in definition.Edges)
        {
            switch (edge.EdgeType)
            {
                case WorkflowEdgeType.Direct:
                    ValidateEndpoint(edge.From, "from", "Direct", errors, knownNodes);
                    ValidateEndpoint(edge.To, "to", "Direct", errors, knownNodes);
                    break;

                case WorkflowEdgeType.Conditional:
                    ValidateEndpoint(edge.From, "from", "Conditional", errors, knownNodes);
                    ValidateEndpoint(edge.To, "to", "Conditional", errors, knownNodes);
                    if (edge.Predicate is null)
                        errors.Add("Edge 'Conditional' requer o campo 'predicate' (com Path + Operator).");
                    break;

                case WorkflowEdgeType.FanOut:
                    ValidateFanOut(edge, errors, knownNodes);
                    break;

                case WorkflowEdgeType.FanIn:
                    ValidateFanIn(edge, errors, knownNodes);
                    break;

                case WorkflowEdgeType.Switch:
                    ValidateSwitch(edge, errors, knownNodes);
                    break;
            }
        }
    }

    private static void ValidateFanOut(WorkflowEdge edge, List<string> errors, HashSet<string> knownNodes)
    {
        ValidateEndpoint(edge.From, "from", "FanOut", errors, knownNodes);

        var targets = edge.Targets.Count > 0 ? edge.Targets
            : edge.To is not null ? [edge.To] : (List<string>)[];

        if (targets.Count == 0)
            errors.Add("Edge 'FanOut' requer 'targets' não vazio (ou 'to').");

        foreach (var t in targets)
            ValidateEndpoint(t, "targets[]", "FanOut", errors, knownNodes);
    }

    private static void ValidateFanIn(WorkflowEdge edge, List<string> errors, HashSet<string> knownNodes)
    {
        ValidateEndpoint(edge.To, "to", "FanIn", errors, knownNodes);

        var sources = edge.Sources.Count > 0 ? edge.Sources
            : edge.From is not null ? [edge.From] : (List<string>)[];

        if (sources.Count == 0)
            errors.Add("Edge 'FanIn' requer 'sources' não vazio (ou 'from').");

        foreach (var s in sources)
            ValidateEndpoint(s, "sources[]", "FanIn", errors, knownNodes);
    }

    private static void ValidateSwitch(WorkflowEdge edge, List<string> errors, HashSet<string> knownNodes)
    {
        ValidateEndpoint(edge.From, "from", "Switch", errors, knownNodes);

        if (edge.Cases.Count == 0)
            errors.Add("Edge 'Switch' requer ao menos 1 item em 'cases'.");

        var hasDefaultOrPredicate = false;
        foreach (var edgeCase in edge.Cases)
        {
            if (edgeCase.Targets.Count == 0)
                errors.Add("Cada case de uma edge 'Switch' deve ter ao menos 1 item em 'targets'.");

            foreach (var t in edgeCase.Targets)
                ValidateEndpoint(t, "cases[].targets[]", "Switch", errors, knownNodes);

            if (!edgeCase.IsDefault && edgeCase.Predicate is null)
                errors.Add("Switch case não-default requer 'predicate'.");

            if (edgeCase.IsDefault || edgeCase.Predicate is not null)
                hasDefaultOrPredicate = true;
        }

        if (edge.Cases.Count > 0 && !hasDefaultOrPredicate)
            errors.Add("Switch precisa ter ao menos 1 case com predicate OU 1 default.");
    }

    private static void ValidateEndpoint(
        string? nodeId,
        string fieldName,
        string edgeType,
        List<string> errors,
        HashSet<string> knownNodes)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            errors.Add($"Edge '{edgeType}': campo '{fieldName}' não pode ser vazio.");
            return;
        }

        if (!knownNodes.Contains(nodeId))
            errors.Add($"Edge '{edgeType}': '{fieldName}' = '{nodeId}' não encontrado nos agentes ou executors do workflow.");
    }
}
