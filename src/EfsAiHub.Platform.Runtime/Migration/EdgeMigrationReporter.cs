using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EfsAiHub.Platform.Runtime.Migration;

/// <summary>
/// Guardrail read-only que detecta edges Conditional/Switch com campo <c>Condition</c>
/// legado vivo no banco. O domínio não conhece mais <c>Condition</c> — qualquer entry
/// retornado significa workflow corrompido (UPDATE manual, restore parcial, deploy
/// fora-de-banda, etc) e bloqueia roteamento tipado.
///
/// Como o modelo C# deletou o campo, lemos <c>Data</c> como JSON cru (System.Text.Json)
/// pra enxergar o blob legado mesmo que a definition não consiga mais fazer round-trip.
/// Esperado em produção: <c>TotalLegacyEntries == 0</c>.
/// </summary>
public sealed class EdgeMigrationReporter
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly ICodeExecutorRegistry _executorRegistry;
    private readonly ILogger<EdgeMigrationReporter> _logger;

    public EdgeMigrationReporter(
        NpgsqlDataSource dataSource,
        IAgentDefinitionRepository agentRepo,
        ICodeExecutorRegistry executorRegistry,
        ILogger<EdgeMigrationReporter>? logger = null)
    {
        _dataSource = dataSource;
        _agentRepo = agentRepo;
        _executorRegistry = executorRegistry;
        _logger = logger ?? NullLogger<EdgeMigrationReporter>.Instance;
    }

    /// <summary>
    /// Gera o relatório completo. Read-only — não modifica nada no banco. Esperado:
    /// <see cref="EdgeMigrationReport.TotalLegacyEntries"/> == 0. Qualquer entry sinaliza
    /// que algum workflow tem <c>Condition</c> vivo (corrupção / bypass do save).
    /// </summary>
    public async Task<EdgeMigrationReport> GenerateAsync(CancellationToken ct = default)
    {
        var rows = await LoadWorkflowsRawAsync(ct);
        var schemaSourceCache = new Dictionary<string, NodeSchemaInfo>(StringComparer.OrdinalIgnoreCase);

        var entries = new List<EdgeMigrationReportEntry>();
        var workflowsWithLegacy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            JsonDocument? doc;
            try
            {
                doc = JsonDocument.Parse(row.Data);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "[EdgeMigrationReport] Workflow '{WorkflowId}' tem Data inválido — ignorando.", row.Id);
                continue;
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("Edges", out var edgesEl)
                    && !doc.RootElement.TryGetProperty("edges", out edgesEl))
                    continue;
                if (edgesEl.ValueKind != JsonValueKind.Array)
                    continue;

                // Mapa local de origem → schema info pra esse workflow.
                var nodeKindMap = ExtractNodeKinds(doc.RootElement);

                var edgeIndex = -1;
                foreach (var edge in edgesEl.EnumerateArray())
                {
                    edgeIndex++;
                    var edgeType = GetString(edge, "EdgeType") ?? GetString(edge, "edgeType");
                    if (!string.Equals(edgeType, "Conditional", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(edgeType, "Switch", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fromNodeId = GetString(edge, "From") ?? GetString(edge, "from");
                    nodeKindMap.TryGetValue(fromNodeId ?? "", out var fromKind);
                    var schemaInfo = await ResolveSchemaInfoAsync(fromNodeId, fromKind, schemaSourceCache, ct);

                    if (string.Equals(edgeType, "Conditional", StringComparison.OrdinalIgnoreCase))
                    {
                        var legacyCondition = GetString(edge, "Condition") ?? GetString(edge, "condition");
                        var hasPredicate = HasNonNullProperty(edge, "Predicate") || HasNonNullProperty(edge, "predicate");

                        if (!hasPredicate && legacyCondition is not null)
                        {
                            entries.Add(BuildEntry(row, edgeIndex, "Conditional", null,
                                fromNodeId, fromKind ?? "unknown",
                                schemaInfo, legacyCondition));
                            workflowsWithLegacy.Add(row.Id);
                        }
                        continue;
                    }

                    // Switch: percorre cases
                    if (!edge.TryGetProperty("Cases", out var casesEl)
                        && !edge.TryGetProperty("cases", out casesEl))
                        continue;
                    if (casesEl.ValueKind != JsonValueKind.Array) continue;

                    var caseIndex = -1;
                    foreach (var c in casesEl.EnumerateArray())
                    {
                        caseIndex++;
                        var legacyCondition = GetString(c, "Condition") ?? GetString(c, "condition");
                        var hasPredicate = HasNonNullProperty(c, "Predicate") || HasNonNullProperty(c, "predicate");
                        var isDefault = GetBool(c, "IsDefault") ?? GetBool(c, "isDefault") ?? false;

                        // Default sem predicate é OK (novo formato). Só reportamos se há Condition legado vivo.
                        if (!hasPredicate && legacyCondition is not null)
                        {
                            entries.Add(BuildEntry(row, edgeIndex, "Switch", caseIndex,
                                fromNodeId, fromKind ?? "unknown",
                                schemaInfo, legacyCondition, isDefault));
                            workflowsWithLegacy.Add(row.Id);
                        }
                    }
                }
            }
        }

        return new EdgeMigrationReport(
            TotalWorkflows: rows.Count,
            WorkflowsWithLegacyEdges: workflowsWithLegacy.Count,
            TotalLegacyEntries: entries.Count,
            Entries: entries);
    }

    private async Task<NodeSchemaInfo> ResolveSchemaInfoAsync(
        string? nodeId, string? kind,
        Dictionary<string, NodeSchemaInfo> cache,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return NodeSchemaInfo.Unknown;
        if (cache.TryGetValue(nodeId, out var cached)) return cached;

        NodeSchemaInfo info = NodeSchemaInfo.Unknown;
        if (kind == "agent")
        {
            var agent = await _agentRepo.GetByIdAsync(nodeId, ct);
            info = agent is null
                ? NodeSchemaInfo.Unknown
                : new NodeSchemaInfo("agent", HasJsonSchema(agent));
        }
        else if (kind == "executor")
        {
            // Executor: schema disponível se função está registrada via Register<TIn,TOut>.
            // O nodeId aqui é o ID interno do executor no workflow — precisamos do FunctionName,
            // que está no JSON da definition. Em vez de re-buscar, deixamos como "unknown" se
            // não conseguimos mapear (caller já passa o kind do nodeKindMap).
            info = new NodeSchemaInfo("executor", false);
        }

        cache[nodeId] = info;
        return info;
    }

    /// <summary>Extrai mapa nodeId → "agent"|"executor" lendo o JSON cru do workflow.</summary>
    private static Dictionary<string, string> ExtractNodeKinds(JsonElement root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if ((root.TryGetProperty("Agents", out var agentsEl) || root.TryGetProperty("agents", out agentsEl))
            && agentsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in agentsEl.EnumerateArray())
            {
                var id = GetString(a, "AgentId") ?? GetString(a, "agentId");
                if (id is not null) map[id] = "agent";
            }
        }

        if ((root.TryGetProperty("Executors", out var execsEl) || root.TryGetProperty("executors", out execsEl))
            && execsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in execsEl.EnumerateArray())
            {
                var id = GetString(e, "Id") ?? GetString(e, "id");
                if (id is not null) map[id] = "executor";
            }
        }

        return map;
    }

    private static EdgeMigrationReportEntry BuildEntry(
        WorkflowRow row, int edgeIndex, string edgeType, int? caseIndex,
        string? fromNodeId, string fromKind,
        NodeSchemaInfo schemaInfo, string legacyCondition, bool isDefaultCase = false)
    {
        var (action, hint) = ClassifyAction(edgeType, fromKind, schemaInfo.HasSchema, isDefaultCase);
        return new EdgeMigrationReportEntry(
            WorkflowId: row.Id,
            WorkflowName: row.Name,
            EdgeIndex: edgeIndex,
            EdgeType: edgeType,
            CaseIndex: caseIndex,
            FromNodeId: fromNodeId,
            FromKind: fromKind,
            HasSchema: schemaInfo.HasSchema,
            LegacyCondition: legacyCondition,
            RecommendedAction: action,
            RecommendationHint: hint);
    }

    private static (string Action, string Hint) ClassifyAction(
        string edgeType, string fromKind, bool hasSchema, bool isDefaultCase)
    {
        // Domínio não aceita mais Condition. Qualquer entry aqui é blob corrompido —
        // a ação é sempre "investigar e remover", não "migrar". Hints variam por kind
        // pra apontar a correção esperada.
        if (hasSchema)
            return ("corrupted_rewrite_predicate",
                $"Origem ({fromKind}) tem schema. Reescrever a edge com Predicate {{Path, Operator, Value}} via UI; depois confirmar que o save validou.");

        if (fromKind == "agent")
            return ("corrupted_no_schema",
                "Origem é agente sem json_schema. Antes de salvar a edge tipada, declarar StructuredOutput.ResponseFormat=\"json_schema\" + Schema mínimo (enum) baseado no domínio.");

        if (fromKind == "executor")
            return ("corrupted_no_schema",
                "Origem é executor destipado. Migrar para Register<TIn,TOut> (ver AtivoExecutorSetup) e então recriar a edge tipada.");

        return ("corrupted_unknown_source",
            "Origem desconhecida — confirmar topologia. Conditional/Switch sem origem schema-aware é proibido pelo domínio.");
    }

    private static bool HasJsonSchema(AgentDefinition agent)
        => agent.StructuredOutput is { Schema: not null, ResponseFormat: "json_schema" or "JSON_SCHEMA" };

    private async Task<IReadOnlyList<WorkflowRow>> LoadWorkflowsRawAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT "Id", "Name", "Data" FROM aihub.workflow_definitions ORDER BY "Id"
            """;

        var list = new List<WorkflowRow>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new WorkflowRow(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Data: reader.GetString(2)));
        }
        return list;
    }

    private static string? GetString(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static bool? GetBool(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool HasNonNullProperty(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return false;
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind != JsonValueKind.Null;
    }

    private record WorkflowRow(string Id, string Name, string Data);

    private readonly record struct NodeSchemaInfo(string Kind, bool HasSchema)
    {
        public static NodeSchemaInfo Unknown => new("unknown", false);
    }
}

/// <summary>Sumário do relatório.</summary>
/// <param name="TotalWorkflows">Workflows lidos do banco.</param>
/// <param name="WorkflowsWithLegacyEdges">Workflows distintos com ao menos 1 edge legado.</param>
/// <param name="TotalLegacyEntries">Total de edges/cases legados (uma linha por edge ou case).</param>
public sealed record EdgeMigrationReport(
    int TotalWorkflows,
    int WorkflowsWithLegacyEdges,
    int TotalLegacyEntries,
    IReadOnlyList<EdgeMigrationReportEntry> Entries);

/// <summary>
/// Linha do relatório por edge (ou case dentro de Switch) que ainda usa <c>Condition</c> legado.
/// </summary>
public sealed record EdgeMigrationReportEntry(
    string WorkflowId,
    string WorkflowName,
    int EdgeIndex,
    string EdgeType,
    int? CaseIndex,
    string? FromNodeId,
    string FromKind,
    bool HasSchema,
    string LegacyCondition,
    string RecommendedAction,
    string RecommendationHint);
