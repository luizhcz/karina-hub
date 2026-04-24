using System.Text.Json;

namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Registra mudanças CRUD em recursos administrativos (Project, Agent, Workflow, Skill,
/// ModelPricing). Fire-and-log: chamadas não devem quebrar o request path — falhas
/// de escrita são logadas como warning e engolidas (auditoria é secundária ao fluxo).
/// </summary>
public interface IAdminAuditLogger
{
    /// <summary>
    /// Persiste um evento de auditoria. Normalmente chamado <b>depois</b> do persist
    /// do recurso principal, para não gerar linhas órfãs em caso de falha do write primário.
    /// </summary>
    Task RecordAsync(AdminAuditEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Consulta paginada por tenant + filtros opcionais. Ordenação: Timestamp DESC
    /// (mais recente primeiro) — espelha o índice IX_admin_audit_log_TenantId_Timestamp.
    /// </summary>
    Task<IReadOnlyList<AdminAuditEntry>> QueryAsync(AdminAuditQuery query, CancellationToken ct = default);

    /// <summary>Conta total de linhas que batem o filtro (sem limit/offset).</summary>
    Task<int> CountAsync(AdminAuditQuery query, CancellationToken ct = default);
}

/// <summary>
/// Entry de auditoria imutável. Campos PayloadBefore/After são opcionais — em creates
/// só há After; em deletes só há Before; em updates preferimos ambos para o diff.
/// </summary>
public sealed class AdminAuditEntry
{
    public long Id { get; init; }
    public string? TenantId { get; init; }
    public string? ProjectId { get; init; }
    public required string ActorUserId { get; init; }
    public string? ActorUserType { get; init; }
    public required string Action { get; init; }         // create | update | delete
    public required string ResourceType { get; init; }   // project | agent | workflow | skill | model_pricing
    public required string ResourceId { get; init; }
    public JsonDocument? PayloadBefore { get; init; }
    public JsonDocument? PayloadAfter { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>Filtros para paginação em /api/admin/audit-log.</summary>
public sealed class AdminAuditQuery
{
    public string? TenantId { get; init; }
    public string? ProjectId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? ActorUserId { get; init; }
    public string? Action { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Constantes para Action e ResourceType — evita typos nos 15+ call sites dos controllers.
/// </summary>
public static class AdminAuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";

    /// <summary>
    /// Leitura administrativa de recurso sensível. Uso seletivo — NÃO
    /// instrumentar todos os GETs (overhead). Começou pela feature persona
    /// (LGPD art. 37 pede trilha de consulta).
    /// </summary>
    public const string Read = "read";
}

public static class AdminAuditResources
{
    public const string Project = "project";
    public const string Agent = "agent";
    public const string Workflow = "workflow";
    public const string Skill = "skill";
    public const string ModelPricing = "model_pricing";
    public const string McpServer = "mcp_server";
    public const string DocumentIntelligencePricing = "document_intelligence_pricing";
    public const string PersonaCache = "persona_cache";
    public const string PersonaPromptTemplate = "persona_prompt_template";
    public const string PersonaPromptExperiment = "persona_prompt_experiment";
}
