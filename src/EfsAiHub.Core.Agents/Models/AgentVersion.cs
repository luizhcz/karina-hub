using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Snapshot imutável e lossless de um agente num determinado ponto do tempo.
/// Captura prompt + model + tools + schema + middlewares em um único blob hashable.
/// <see cref="ToDefinition"/> reconstrói AgentDefinition determinístico.
///
/// Append-only: UpsertAsync de AgentDefinition cria um novo AgentVersion com Revision = MAX+1
/// e atualiza o ponteiro na mesma transação. Rollback determinístico = apontar
/// CurrentVersionId de AgentDefinition para uma revision anterior.
/// </summary>
public sealed record AgentVersion(
    string AgentVersionId,
    string AgentDefinitionId,
    int Revision,
    DateTime CreatedAt,
    string? CreatedBy,
    string? ChangeReason,
    AgentVersionStatus Status,
    string? PromptContent,
    string? PromptVersionId,
    AgentModelSnapshot Model,
    AgentProviderSnapshot Provider,
    IReadOnlyList<AgentMiddlewareSnapshot> MiddlewarePipeline,
    AgentStructuredOutputSnapshot? OutputSchema,
    ResiliencePolicy? Resilience,
    AgentCostBudget? CostBudget,
    IReadOnlyList<SkillRef> SkillRefs,
    string ContentHash,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    AgentProviderSnapshot? FallbackProvider = null,
    IReadOnlyList<AgentToolSnapshot>? Tools = null,
    bool BreakingChange = false,
    AgentOperationalMemorySnapshot? OperationalMemory = null)
{
    /// <summary>
    /// Constrói um snapshot a partir de uma AgentDefinition viva + conteúdo de prompt resolvido.
    /// Calcula ContentHash canônico (sha256) para rollback idempotente.
    /// </summary>
    public static AgentVersion FromDefinition(
        AgentDefinition definition,
        int revision,
        string? promptContent,
        string? promptVersionId,
        string? createdBy = null,
        string? changeReason = null,
        IReadOnlyList<SkillRef>? skillRefs = null,
        bool breakingChange = false)
    {
        var model = new AgentModelSnapshot(
            definition.Model.DeploymentName,
            definition.Model.Temperature,
            definition.Model.MaxTokens,
            definition.Model.PredefinedModelId);

        var provider = new AgentProviderSnapshot(
            definition.Provider.Type,
            definition.Provider.ClientType,
            definition.Provider.Endpoint,
            HasValue: !string.IsNullOrEmpty(definition.Provider.ApiKey));

        AgentProviderSnapshot? fallbackProvider = null;
        if (definition.FallbackProvider is { } fb)
        {
            fallbackProvider = new AgentProviderSnapshot(
                fb.Type,
                fb.ClientType,
                fb.Endpoint,
                HasValue: !string.IsNullOrEmpty(fb.ApiKey));
        }

        var tools = definition.Tools
            .Select(t => AgentToolSnapshot.FromDefinition(t))
            .ToList();

        // Projeção canônica em três níveis crescentes pra preservar ContentHash
        // de agents históricos sem campos novos:
        //   * Tool sem GenericToolId nem expansão HTTP → formato legacy compacto.
        //   * Tool com GenericToolId mas sem expansão → adiciona apenas o id (formato v2).
        //   * Tool com expansão HTTP populada → adiciona todos os campos resolvidos
        //     (mudanças no schema/url/headers expandidos geram hash novo, intencional).
        var canonicalTools = tools
            .Select<AgentToolSnapshot, object>(t =>
            {
                if (t.GenericToolId is null)
                {
                    return new
                    {
                        t.Type,
                        t.Name,
                        t.RequiresApproval,
                        t.FingerprintHash,
                        t.McpServerId,
                        t.ServerLabel,
                        t.ServerUrl,
                        t.AllowedTools,
                        t.RequireApproval,
                        t.Headers,
                        t.ConnectionId,
                    };
                }

                var hasExpansion =
                    t.HttpMethod is not null
                    || t.UrlTemplate is not null
                    || (t.PathParams?.Count ?? 0) > 0
                    || (t.QueryParams?.Count ?? 0) > 0
                    || (t.CustomHeaders?.Count ?? 0) > 0
                    || t.InputContentType is not null
                    || t.InputSchemaJson is not null
                    || t.OutputContentType is not null
                    || t.OutputSchemaJson is not null
                    || t.OutputProjectionMode is not null
                    || t.TimeoutSecondsOverride is not null
                    || t.WhenToUse is not null
                    || t.IsExclusive is not null
                    || t.Description is not null;

                if (!hasExpansion)
                {
                    return new
                    {
                        t.Type,
                        t.Name,
                        t.RequiresApproval,
                        t.FingerprintHash,
                        t.McpServerId,
                        t.ServerLabel,
                        t.ServerUrl,
                        t.AllowedTools,
                        t.RequireApproval,
                        t.Headers,
                        t.ConnectionId,
                        t.GenericToolId,
                    };
                }

                return new
                {
                    t.Type,
                    t.Name,
                    t.RequiresApproval,
                    t.FingerprintHash,
                    t.McpServerId,
                    t.ServerLabel,
                    t.ServerUrl,
                    t.AllowedTools,
                    t.RequireApproval,
                    t.Headers,
                    t.ConnectionId,
                    t.GenericToolId,
                    t.Description,
                    t.HttpMethod,
                    t.UrlTemplate,
                    t.PathParams,
                    t.QueryParams,
                    t.CustomHeaders,
                    t.InputContentType,
                    t.InputSchemaJson,
                    t.OutputContentType,
                    t.OutputSchemaJson,
                    t.OutputProjectionMode,
                    t.TimeoutSecondsOverride,
                    t.WhenToUse,
                    t.IsExclusive,
                };
            })
            .ToList();

        // Projeção canônica: agent sem preset serializa model exatamente como
        // antes (sem PredefinedModelId no JSON) — preserva ContentHash de agents
        // existentes. Agent com preset adiciona o campo no final, gerando hash
        // novo intencionalmente.
        var canonicalModel = model.PredefinedModelId is null
            ? (object)new
            {
                model.DeploymentName,
                model.Temperature,
                model.MaxTokens,
            }
            : new
            {
                model.DeploymentName,
                model.Temperature,
                model.MaxTokens,
                model.PredefinedModelId,
            };

        var middlewares = definition.Middlewares
            .Select(m => new AgentMiddlewareSnapshot(m.Type, m.Enabled, new Dictionary<string, string>(m.Settings)))
            .ToList();

        AgentStructuredOutputSnapshot? outputSchema = null;
        if (definition.StructuredOutput is not null)
        {
            outputSchema = new AgentStructuredOutputSnapshot(
                definition.StructuredOutput.ResponseFormat,
                definition.StructuredOutput.SchemaName,
                definition.StructuredOutput.SchemaDescription,
                definition.StructuredOutput.Schema?.RootElement.GetRawText());
        }

        AgentOperationalMemorySnapshot? operationalMemory = null;
        if (definition.OperationalMemory is not null)
        {
            operationalMemory = new AgentOperationalMemorySnapshot(
                definition.OperationalMemory.Schema?.RootElement.GetRawText(),
                definition.OperationalMemory.MaxBytes);
        }

        IReadOnlyDictionary<string, string>? metadata = definition.Metadata.Count == 0
            ? null
            : new Dictionary<string, string>(definition.Metadata);

        // Type sempre incluído no canonical (JsonStringEnumConverter global serializa
        // como string). Agentes legacy sem campo Type no jsonb hidratam como Custom e
        // geram ContentHash novo na primeira edição pós-feature — mudança one-time,
        // sem regressão recorrente. Snapshots existentes em agent_versions ficam
        // intactos por serem append-only.
        var canonical = JsonSerializer.Serialize(new
        {
            agentId = definition.Id,
            description = definition.Description,
            metadata,
            type = definition.Type,
            prompt = promptContent,
            model = canonicalModel,
            provider = new { provider.Type, provider.ClientType, provider.Endpoint, provider.HasValue },
            fallbackProvider = fallbackProvider is null
                ? null
                : new
                {
                    fallbackProvider.Type,
                    fallbackProvider.ClientType,
                    fallbackProvider.Endpoint,
                    fallbackProvider.HasValue,
                },
            tools = canonicalTools,
            middlewares,
            outputSchema,
            // Sem operationalMemory no canonical, mudanças em memory passam
            // pelo dedup do AppendAsync (mesmo ContentHash) e o runtime
            // continua usando snapshot anterior sem schema — bug invisível.
            operationalMemory,
            resilience = definition.Resilience,
            costBudget = definition.CostBudget,
            skills = skillRefs ?? (IReadOnlyList<SkillRef>)definition.SkillRefs
        }, JsonDefaults.Domain);

        var hash = ComputeSha256(canonical);

        return new AgentVersion(
            AgentVersionId: Guid.NewGuid().ToString("N"),
            AgentDefinitionId: definition.Id,
            Revision: revision,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: createdBy,
            ChangeReason: changeReason,
            Status: AgentVersionStatus.Published,
            PromptContent: promptContent,
            PromptVersionId: promptVersionId,
            Model: model,
            Provider: provider,
            MiddlewarePipeline: middlewares,
            OutputSchema: outputSchema,
            Resilience: definition.Resilience,
            CostBudget: definition.CostBudget,
            SkillRefs: skillRefs ?? (IReadOnlyList<SkillRef>)definition.SkillRefs,
            ContentHash: hash,
            Description: definition.Description,
            Metadata: metadata,
            FallbackProvider: fallbackProvider,
            Tools: tools,
            BreakingChange: breakingChange,
            OperationalMemory: operationalMemory);
    }

    /// <summary>
    /// Reconstrói <see cref="AgentDefinition"/> determinístico a partir do snapshot.
    /// <paramref name="governanceSource"/> (opcional) injeta Visibility/ProjectId/TenantId/AllowedProjectIds
    /// da row corrente — esses campos são cross-cutting e mutáveis (mudança de visibility do owner
    /// deve afetar workflows pinados). Quando null, defaults seguros: project-scoped, default tenant,
    /// sem whitelist.
    /// </summary>
    public AgentDefinition ToDefinition(AgentDefinition? governanceSource = null)
    {
        var modelConfig = new AgentModelConfig
        {
            DeploymentName = Model.DeploymentName,
            Temperature = Model.Temperature,
            MaxTokens = Model.MaxTokens,
            PredefinedModelId = Model.PredefinedModelId,
        };

        // ApiKey não é persistida no snapshot. Hidratada em runtime via InjectProjectCredentials
        // (lê do owner project). Endpoint/Type/ClientType vêm do snapshot.
        var providerConfig = new AgentProviderConfig
        {
            Type = Provider.Type,
            ClientType = Provider.ClientType,
            Endpoint = Provider.Endpoint,
        };

        AgentProviderConfig? fallbackConfig = null;
        if (FallbackProvider is { } fb)
        {
            fallbackConfig = new AgentProviderConfig
            {
                Type = fb.Type,
                ClientType = fb.ClientType,
                Endpoint = fb.Endpoint,
            };
        }

        var tools = Tools is null
            ? Array.Empty<AgentToolDefinition>()
            : Tools.Select(t => t.ToDefinition()).ToList().AsReadOnly() as IReadOnlyList<AgentToolDefinition>;

        AgentStructuredOutputDefinition? outputDef = null;
        if (OutputSchema is { } output)
        {
            outputDef = new AgentStructuredOutputDefinition
            {
                ResponseFormat = output.ResponseFormat,
                SchemaName = output.SchemaName,
                SchemaDescription = output.SchemaDescription,
                Schema = TryParseSchema(output.SchemaJson),
            };
        }

        AgentOperationalMemoryDefinition? memoryDef = null;
        if (OperationalMemory is { } memory && memory.SchemaJson is not null)
        {
            var parsed = TryParseSchema(memory.SchemaJson);
            if (parsed is not null)
            {
                memoryDef = new AgentOperationalMemoryDefinition
                {
                    Schema = parsed,
                    MaxBytes = memory.MaxBytes,
                };
            }
            // Snapshot com SchemaJson malformado é incidente raro (DB corruption ou
            // serializer bug em release antigo). Degrada silenciosamente pra
            // memory desligada em vez de derrubar a hidratação do agente inteiro.
        }

        var middlewares = MiddlewarePipeline
            .Select(m => new AgentMiddlewareConfig
            {
                Type = m.Type,
                Enabled = m.Enabled,
                Settings = new Dictionary<string, string>(m.Settings),
            })
            .ToList();

        IReadOnlyDictionary<string, string> metadata = Metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(Metadata);

        return new AgentDefinition
        {
            Id = AgentDefinitionId,
            Name = governanceSource?.Name ?? AgentDefinitionId,
            Description = Description,
            // Type não viaja no snapshot lossless — é estado mutável da row
            // de governança (a tipologia é decidida no create e preservada em
            // UpdateAsync). Hidrata do governanceSource quando disponível;
            // default Custom é seguro pra agentes legacy pré-tipologia.
            Type = governanceSource?.Type ?? AgentType.Custom,
            Model = modelConfig,
            Provider = providerConfig,
            FallbackProvider = fallbackConfig,
            Instructions = PromptContent,
            Tools = tools,
            StructuredOutput = outputDef,
            OperationalMemory = memoryDef,
            Middlewares = middlewares,
            Resilience = Resilience,
            CostBudget = CostBudget,
            SkillRefs = SkillRefs,
            Metadata = metadata,
            // Governança vem do estado vivo (mutável, cross-cutting).
            ProjectId = governanceSource?.ProjectId ?? "default",
            Visibility = governanceSource?.Visibility ?? "project",
            TenantId = governanceSource?.TenantId ?? "default",
            AllowedProjectIds = governanceSource?.AllowedProjectIds,
            CreatedAt = governanceSource?.CreatedAt ?? CreatedAt,
            UpdatedAt = governanceSource?.UpdatedAt ?? CreatedAt,
            RegressionTestSetId = governanceSource?.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId = governanceSource?.RegressionEvaluatorConfigVersionId,
        };
    }

    /// <summary>
    /// Valida invariantes do snapshot. Idempotente.
    /// </summary>
    /// <exception cref="DomainException">Quando alguma invariante é violada.</exception>
    /// <summary>
    /// Tenta parsear o JSON Schema persistido no snapshot. Retorna null em
    /// payload malformado pra degradar silenciosamente em vez de derrubar a
    /// hidratação do agente inteiro. Snapshot corrompido em DB é incidente
    /// raro (rollback de seed antigo, corruption manual); não justifica
    /// indisponibilizar o agente — perder OutputSchema/OperationalMemory
    /// volta o agente pro modo legacy sem essa configuração.
    /// </summary>
    private static JsonDocument? TryParseSchema(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson)) return null;
        try
        {
            return JsonDocument.Parse(schemaJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void EnsureInvariants()
    {
        if (string.IsNullOrWhiteSpace(AgentVersionId))
            throw new DomainException("AgentVersion.AgentVersionId é obrigatório.");
        if (string.IsNullOrWhiteSpace(AgentDefinitionId))
            throw new DomainException("AgentVersion.AgentDefinitionId é obrigatório.");
        if (string.IsNullOrWhiteSpace(ContentHash))
            throw new DomainException("AgentVersion.ContentHash é obrigatório.");

        // BreakingChange=true exige ChangeReason explícito (rastreabilidade pra caller decidir migrar).
        if (BreakingChange && string.IsNullOrWhiteSpace(ChangeReason))
            throw new DomainException(
                "AgentVersion.BreakingChange=true exige ChangeReason não-vazio (justifica a quebra pra workflows pinados).");
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

public enum AgentVersionStatus
{
    Draft,
    Published,
    Retired
}

/// <summary>
/// Snapshot lossless de uma <see cref="AgentToolDefinition"/>. Persistido em
/// <see cref="AgentVersion.Tools"/> pra reconstrução determinística via <see cref="ToDefinition"/>.
/// Carrega TODOS os campos da tool — não há perda na ida/volta. Tools do tipo
/// <c>generic_http</c> incluem a expansão completa da configuração HTTP
/// (UrlTemplate, schemas, headers fixos, etc) populada no momento do publish
/// pelo composer — runtime nunca volta ao repositório de tools.
/// </summary>
public sealed record AgentToolSnapshot(
    string Type,
    string? Name,
    bool RequiresApproval,
    string? FingerprintHash,
    string? McpServerId,
    string? ServerLabel,
    string? ServerUrl,
    IReadOnlyList<string> AllowedTools,
    string? RequireApproval,
    IReadOnlyDictionary<string, string> Headers,
    string? ConnectionId,
    string? GenericToolId,
    string? Description = null,
    HttpMethodType? HttpMethod = null,
    string? UrlTemplate = null,
    IReadOnlyDictionary<string, ParamDefinition>? PathParams = null,
    IReadOnlyDictionary<string, ParamDefinition>? QueryParams = null,
    IReadOnlyDictionary<string, string>? CustomHeaders = null,
    InputContentType? InputContentType = null,
    string? InputSchemaJson = null,
    OutputContentType? OutputContentType = null,
    string? OutputSchemaJson = null,
    OutputProjectionMode? OutputProjectionMode = null,
    int? TimeoutSecondsOverride = null,
    string? WhenToUse = null,
    bool? IsExclusive = null)
{
    public static AgentToolSnapshot FromDefinition(AgentToolDefinition tool) => new(
        Type: tool.Type,
        Name: tool.Name,
        RequiresApproval: tool.RequiresApproval,
        FingerprintHash: tool.FingerprintHash,
        McpServerId: tool.McpServerId,
        ServerLabel: tool.ServerLabel,
        ServerUrl: tool.ServerUrl,
        // Headers ordenado por chave: garante hash canônico independente da ordem
        // de inserção do Dictionary original (defesa contra divergência cross-instância).
        AllowedTools: tool.AllowedTools.ToList(),
        RequireApproval: tool.RequireApproval,
        Headers: tool.Headers
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .ToDictionary(h => h.Key, h => h.Value, StringComparer.Ordinal),
        ConnectionId: tool.ConnectionId,
        GenericToolId: tool.GenericToolId,
        Description: tool.Description,
        HttpMethod: tool.HttpMethod,
        UrlTemplate: tool.UrlTemplate,
        // Path/QueryParams e CustomHeaders também ordenados pra hash estável
        // independente da ordem de inserção do dicionário original.
        PathParams: tool.PathParams is null
            ? null
            : tool.PathParams
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        QueryParams: tool.QueryParams is null
            ? null
            : tool.QueryParams
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        CustomHeaders: tool.CustomHeaders is null
            ? null
            : tool.CustomHeaders
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        InputContentType: tool.InputContentType,
        InputSchemaJson: tool.InputSchemaJson,
        OutputContentType: tool.OutputContentType,
        OutputSchemaJson: tool.OutputSchemaJson,
        OutputProjectionMode: tool.OutputProjectionMode,
        TimeoutSecondsOverride: tool.TimeoutSecondsOverride,
        WhenToUse: tool.WhenToUse,
        IsExclusive: tool.IsExclusive);

    public AgentToolDefinition ToDefinition() => new()
    {
        Type = Type,
        Name = Name,
        RequiresApproval = RequiresApproval,
        FingerprintHash = FingerprintHash,
        McpServerId = McpServerId,
        ServerLabel = ServerLabel,
        ServerUrl = ServerUrl,
        AllowedTools = new List<string>(AllowedTools),
        RequireApproval = RequireApproval,
        Headers = new Dictionary<string, string>(Headers),
        ConnectionId = ConnectionId,
        GenericToolId = GenericToolId,
        Description = Description,
        HttpMethod = HttpMethod,
        UrlTemplate = UrlTemplate,
        PathParams = PathParams,
        QueryParams = QueryParams,
        CustomHeaders = CustomHeaders,
        InputContentType = InputContentType,
        InputSchemaJson = InputSchemaJson,
        OutputContentType = OutputContentType,
        OutputSchemaJson = OutputSchemaJson,
        OutputProjectionMode = OutputProjectionMode,
        TimeoutSecondsOverride = TimeoutSecondsOverride,
        WhenToUse = WhenToUse,
        IsExclusive = IsExclusive,
    };
}

public sealed record AgentModelSnapshot(
    string DeploymentName,
    float? Temperature,
    int? MaxTokens,
    string? PredefinedModelId = null);

public sealed record AgentProviderSnapshot(
    string Type,
    string ClientType,
    string? Endpoint,
    bool HasValue);

public sealed record AgentMiddlewareSnapshot(
    string Type,
    bool Enabled,
    Dictionary<string, string> Settings);

public sealed record AgentStructuredOutputSnapshot(
    string ResponseFormat,
    string? SchemaName,
    string? SchemaDescription,
    string? SchemaJson);

/// <summary>
/// Snapshot lossless de <see cref="AgentOperationalMemoryDefinition"/>. Sem
/// este snapshot, mudanças em memory ficam fora do <c>ContentHash</c> e
/// <c>AgentVersionRepository.AppendAsync</c> faz dedup silencioso da
/// revisão — runtime continua usando a versão antiga sem schema e o
/// middleware <c>OperationalMemoryChatClient</c> não ativa.
/// </summary>
public sealed record AgentOperationalMemorySnapshot(
    string? SchemaJson,
    int? MaxBytes);
