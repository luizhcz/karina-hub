using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Snapshot imutável de um agente num determinado ponto do tempo.
/// Captura prompt + model + tools + schema + middlewares em um único blob hashable.
/// Rollback determinístico = apontar CurrentVersionId de AgentDefinition para uma revision anterior.
///
/// Append-only: UpsertAsync de AgentDefinition cria um novo AgentVersion com Revision = MAX+1
/// e atualiza o ponteiro na mesma transação.
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
    IReadOnlyList<ToolFingerprint> ToolFingerprints,
    IReadOnlyList<AgentMiddlewareSnapshot> MiddlewarePipeline,
    AgentStructuredOutputSnapshot? OutputSchema,
    // Política de retry e orçamento de custo capturados no snapshot.
    ResiliencePolicy? Resilience,
    AgentCostBudget? CostBudget,
    // Referências a skills materializadas com SkillVersionId explícito para rollback determinístico.
    IReadOnlyList<SkillRef> SkillRefs,
    string ContentHash)
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
        IReadOnlyList<SkillRef>? skillRefs = null)
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

        var fingerprints = definition.Tools
            .Select(t => ToolFingerprint.FromDefinition(t))
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

        var canonical = JsonSerializer.Serialize(new
        {
            agentId = definition.Id,
            prompt = promptContent,
            model,
            provider = new { provider.Type, provider.ClientType, provider.Endpoint, provider.HasValue },
            tools = fingerprints,
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
            ToolFingerprints: fingerprints,
            MiddlewarePipeline: middlewares,
            OutputSchema: outputSchema,
            Resilience: definition.Resilience,
            CostBudget: definition.CostBudget,
            SkillRefs: skillRefs ?? (IReadOnlyList<SkillRef>)definition.SkillRefs,
            ContentHash: hash);
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
/// Identifica uma tool referenciada por um AgentVersion. Quando a tool ainda não
/// carrega fingerprint canônico (sha256 do JSONSchema), o SignatureHash é derivado
/// dos campos declarativos do AgentToolDefinition como fallback.
/// </summary>
public sealed record ToolFingerprint(
    string Type,
    string? Name,
    string SignatureHash,
    string? ServerLabel = null,
    string? ServerUrl = null)
{
    public static ToolFingerprint FromDefinition(AgentToolDefinition tool)
    {
        // Quando o registry já populou o fingerprint canônico (sha256 do JSONSchema),
        // prefere-o ao hash declarativo derivado dos campos.
        if (!string.IsNullOrEmpty(tool.FingerprintHash))
        {
            return new ToolFingerprint(
                Type: tool.Type,
                Name: tool.Name,
                SignatureHash: tool.FingerprintHash!,
                ServerLabel: tool.ServerLabel,
                ServerUrl: tool.ServerUrl);
        }

        var canonical = JsonSerializer.Serialize(new
        {
            tool.Type,
            tool.Name,
            tool.RequiresApproval,
            tool.ServerLabel,
            tool.ServerUrl,
            tool.AllowedTools,
            tool.RequireApproval,
            tool.ConnectionId
        }, JsonDefaults.Domain);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        return new ToolFingerprint(
            Type: tool.Type,
            Name: tool.Name,
            SignatureHash: hash,
            ServerLabel: tool.ServerLabel,
            ServerUrl: tool.ServerUrl);
    }
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
