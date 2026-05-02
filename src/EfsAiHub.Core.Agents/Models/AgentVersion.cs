using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Exceptions;
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
    bool BreakingChange = false)
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
            definition.Model.MaxTokens);

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

        IReadOnlyDictionary<string, string>? metadata = definition.Metadata.Count == 0
            ? null
            : new Dictionary<string, string>(definition.Metadata);

        var canonical = JsonSerializer.Serialize(new
        {
            agentId = definition.Id,
            description = definition.Description,
            metadata,
            prompt = promptContent,
            model,
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
            tools,
            middlewares,
            outputSchema,
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
            BreakingChange: breakingChange);
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
                Schema = output.SchemaJson is null ? null : JsonDocument.Parse(output.SchemaJson),
            };
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
            Model = modelConfig,
            Provider = providerConfig,
            FallbackProvider = fallbackConfig,
            Instructions = PromptContent,
            Tools = tools,
            StructuredOutput = outputDef,
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
/// Carrega TODOS os campos da tool — não há perda na ida/volta.
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
    string? ConnectionId)
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
        ConnectionId: tool.ConnectionId);

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
    };
}

public sealed record AgentModelSnapshot(
    string DeploymentName,
    float? Temperature,
    int? MaxTokens);

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
