using System.Text.Json;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Interfaces;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Valida invariantes tipadas de edges Conditional/Switch que <see cref="EdgeValidator"/>
/// não cobre — regras de negócio que cruzam o registry de agentes e executors.
///
/// Em particular: Conditional/Switch só pode sair de origem com schema declarado
/// (agente <c>StructuredOutput.ResponseFormat == "json_schema"</c> ou executor
/// <c>Register&lt;TIn,TOut&gt;</c>). Ausência de schema → 400 <c>EdgeNotAllowedFromTextSource</c>.
///
/// Coletamos todas as violações em <see cref="WorkflowInvariantError"/>; controller
/// converte em response 400 com envelope estruturado.
/// </summary>
public sealed class EdgeInvariantsValidator
{
    private static readonly Regex JsonPathRegex = new(
        @"^\$(\.[A-Za-z_][A-Za-z0-9_]*|\[\d+\])*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Tamanho máximo de pattern regex aceito em <c>MatchesRegex</c>. Defesa contra DoS via pattern catastrófico no save.</summary>
    private const int MaxRegexPatternLength = 1024;

    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly ICodeExecutorRegistry _executorRegistry;

    public EdgeInvariantsValidator(
        IAgentDefinitionRepository agentRepo,
        ICodeExecutorRegistry executorRegistry)
    {
        _agentRepo = agentRepo;
        _executorRegistry = executorRegistry;
    }

    /// <summary>
    /// Valida o workflow contra as invariantes tipadas. Retorna lista vazia se válido.
    /// Só roda em modo Graph — outros modos não usam Conditional/Switch tipado.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowInvariantError>> ValidateAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        var errors = new List<WorkflowInvariantError>();
        if (definition.OrchestrationMode != OrchestrationMode.Graph || definition.Edges.Count == 0)
            return errors;

        var schemaSources = await BuildSchemaSourceMapAsync(definition, ct);

        for (var i = 0; i < definition.Edges.Count; i++)
        {
            var edge = definition.Edges[i];
            switch (edge.EdgeType)
            {
                case WorkflowEdgeType.Conditional:
                    ValidateConditional(edge, i, schemaSources, errors);
                    break;
                case WorkflowEdgeType.Switch:
                    ValidateSwitch(edge, i, schemaSources, errors);
                    break;
            }
        }

        return errors;
    }

    private static bool HasSchemaInMap(string? nodeId, IReadOnlyDictionary<string, JsonElement?> map)
        => !string.IsNullOrWhiteSpace(nodeId) && map.TryGetValue(nodeId, out var v) && v.HasValue;

    private static JsonElement? GetSchema(string? nodeId, IReadOnlyDictionary<string, JsonElement?> map)
        => nodeId is not null && map.TryGetValue(nodeId, out var v) ? v : null;

    private void ValidateConditional(
        WorkflowEdge edge, int edgeIndex,
        IReadOnlyDictionary<string, JsonElement?> schemaSources,
        List<WorkflowInvariantError> errors)
    {
        if (edge.Predicate is null)
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.EdgePredicateRequired,
                $"Edge Conditional {edge.From} → {edge.To} sem 'predicate'.",
                "Adicione um predicate (Path, Operator, Value) ou troque o tipo da aresta para Direct.",
                edgeIndex));
            return;
        }

        if (!HasSchemaInMap(edge.From, schemaSources))
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.EdgeNotAllowedFromTextSource,
                $"Edge Conditional saindo de '{edge.From}' não é permitido — origem não declara schema JSON.",
                "Torne o agente structured (json_schema) ou registre o executor via Register<TIn,TOut>. Alternativa: troque a aresta para Direct.",
                edgeIndex));
        }

        ValidatePredicate(edge.Predicate, edgeIndex, GetSchema(edge.From, schemaSources), edge.From, errors);
    }

    private void ValidateSwitch(
        WorkflowEdge edge, int edgeIndex,
        IReadOnlyDictionary<string, JsonElement?> schemaSources,
        List<WorkflowInvariantError> errors)
    {
        if (edge.Cases.Count == 0)
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.SwitchHasNoCaseOrDefault,
                $"Edge Switch saindo de '{edge.From}' sem nenhum case.",
                "Adicione ao menos um case com predicate ou um default.",
                edgeIndex));
            return;
        }

        if (!HasSchemaInMap(edge.From, schemaSources))
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.EdgeNotAllowedFromTextSource,
                $"Edge Switch saindo de '{edge.From}' não é permitido — origem não declara schema JSON.",
                "Torne o agente structured (json_schema) ou registre o executor via Register<TIn,TOut>. Alternativa: troque a aresta para Direct.",
                edgeIndex));
        }

        var fromSchema = GetSchema(edge.From, schemaSources);
        var hasDefault = false;
        var hasAnyPredicate = false;
        foreach (var c in edge.Cases)
        {
            if (c.IsDefault) hasDefault = true;
            if (c.Predicate is not null)
            {
                hasAnyPredicate = true;
                ValidatePredicate(c.Predicate, edgeIndex, fromSchema, edge.From, errors);
            }
            else if (!c.IsDefault)
            {
                errors.Add(new WorkflowInvariantError(
                    WorkflowErrorCodes.EdgePredicateRequired,
                    "Switch case não-default sem 'predicate'.",
                    "Defina o predicate do case ou marque-o como default (IsDefault=true).",
                    edgeIndex));
            }
        }

        if (!hasDefault && !hasAnyPredicate)
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.SwitchHasNoCaseOrDefault,
                "Switch precisa ter ao menos 1 case com predicate OU 1 default.",
                "Adicione um predicate em algum case ou marque um como default.",
                edgeIndex));
        }
    }

    private static void ValidatePredicate(
        EdgePredicate predicate, int edgeIndex,
        JsonElement? fromSchema, string? fromNodeId,
        List<WorkflowInvariantError> errors)
    {
        var pathSyntaxValid = !string.IsNullOrWhiteSpace(predicate.Path) && JsonPathRegex.IsMatch(predicate.Path);
        if (!pathSyntaxValid)
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.InvalidJsonPath,
                $"JSONPath '{predicate.Path}' fora do subset suportado.",
                "Use $, $.field, $.a.b, $.list[N] ou $.list[N].field. Wildcards, filtros e índice negativo não são suportados.",
                edgeIndex));
        }

        // Cross-check do path contra schema atual do produtor (PR 3 fechando dívida do PR 2).
        // Só roda quando syntax do path é válida E temos schema disponível — evita ruído duplicado.
        if (pathSyntaxValid && fromSchema is { ValueKind: JsonValueKind.Object } schema
            && !JsonSchemaPathResolver.PathExistsInSchema(schema, predicate.Path))
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.PathNotFoundInSchema,
                $"Path '{predicate.Path}' não resolve nenhum campo do schema atual de '{fromNodeId}'.",
                "Conferir o schema do produtor (json_schema do agente ou OutputType do executor) e ajustar o Path; campo pode ter sido renomeado/removido.",
                edgeIndex));
        }

        if (!IsOperatorValidForType(predicate.Operator, predicate.ValueType))
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.InvalidOperatorForType,
                $"Operador '{predicate.Operator}' inválido para tipo '{predicate.ValueType}'.",
                "Operadores numéricos (Gt/Gte/Lt/Lte) só aceitam Number/Integer; Contains/StartsWith/EndsWith/MatchesRegex só aceitam String.",
                edgeIndex));
        }

        // Defesa anti-DoS: regex catastrófica no pattern pode travar o thread
        // durante compilação (antes de qualquer timeout de match). Limite no save.
        if (predicate.Operator == EdgeOperator.MatchesRegex
            && predicate.Value is { ValueKind: JsonValueKind.String } v
            && v.GetString() is { Length: > MaxRegexPatternLength } longPattern)
        {
            errors.Add(new WorkflowInvariantError(
                WorkflowErrorCodes.InvalidOperatorForType,
                $"Pattern regex excede o limite de {MaxRegexPatternLength} chars (atual: {longPattern.Length}).",
                "Simplifique o pattern. Patterns grandes são mitigação de DoS — use múltiplos cases mais simples se precisar de muitas alternativas.",
                edgeIndex));
        }
    }

    private static bool IsOperatorValidForType(EdgeOperator op, EdgePredicateValueType type)
    {
        // Auto = runtime detecta — aceita qualquer operator (validação cai pro evaluator).
        if (type == EdgePredicateValueType.Auto) return true;

        return op switch
        {
            EdgeOperator.Eq or EdgeOperator.NotEq or EdgeOperator.IsNull or EdgeOperator.IsNotNull
                or EdgeOperator.In or EdgeOperator.NotIn => true,

            EdgeOperator.Gt or EdgeOperator.Gte or EdgeOperator.Lt or EdgeOperator.Lte =>
                type == EdgePredicateValueType.Number || type == EdgePredicateValueType.Integer,

            EdgeOperator.Contains or EdgeOperator.StartsWith or EdgeOperator.EndsWith
                or EdgeOperator.MatchesRegex =>
                type == EdgePredicateValueType.String,

            _ => false
        };
    }

    /// <summary>
    /// Constrói mapa nodeId → schema (JsonElement?). Null = origem sem schema (texto livre,
    /// executor destipado). Tendo o JsonElement permite cross-check do Path do predicate.
    ///
    /// Performance: agentIds são consultados em paralelo (Task.WhenAll). Pool do Npgsql
    /// serializa por conexão mas múltiplas conexões executam simultaneamente — 8 agentes
    /// custam ~1-2 round-trips em vez de 8 sequenciais.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, JsonElement?>> BuildSchemaSourceMapAsync(
        WorkflowDefinition definition, CancellationToken ct)
    {
        var map = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);

        // Agentes em paralelo — N+1 sequencial era hot path no save de workflow grande.
        var agentIds = definition.Agents.Select(a => a.AgentId).Distinct().ToArray();
        var agentDefs = await Task.WhenAll(
            agentIds.Select(id => _agentRepo.GetByIdAsync(id, ct)));

        for (var i = 0; i < agentIds.Length; i++)
            map[agentIds[i]] = ExtractAgentSchema(agentDefs[i]);

        // Executores: schema vem do registry (sha + JsonElement gerados via JsonSchemaExporter no PR 1).
        var executorSchemas = _executorRegistry.GetSchemas();
        foreach (var ex in definition.Executors)
        {
            JsonElement? schema = null;
            if (!string.IsNullOrEmpty(ex.FunctionName)
                && executorSchemas.TryGetValue(ex.FunctionName, out var info))
            {
                schema = info.OutputSchema;
            }
            map[ex.Id] = schema;
        }

        return map;
    }

    private static JsonElement? ExtractAgentSchema(AgentDefinition? agent)
    {
        if (agent?.StructuredOutput is null) return null;
        if (!string.Equals(agent.StructuredOutput.ResponseFormat, "json_schema", StringComparison.OrdinalIgnoreCase))
            return null;
        if (agent.StructuredOutput.Schema is null) return null;

        // JsonDocument do StructuredOutput é gerenciado pela definição do agente (não disposamos aqui).
        // Clone pra desacoplar do buffer do Document caso o caller alterne escopo da definition.
        return agent.StructuredOutput.Schema.RootElement.Clone();
    }
}
