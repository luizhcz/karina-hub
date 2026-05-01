using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EfsAiHub.Infra.Persistence.Postgres;

internal class WorkflowDefinitionRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Data { get; set; } = "{}";
    public string ProjectId { get; set; } = "default";
    /// <summary>"project" | "global". Visível só dentro do tenant quando "global".</summary>
    public string Visibility { get; set; } = "project";
    /// <summary>Denormalizado de projects.tenant_id; populado no upsert pelo repository.</summary>
    public string TenantId { get; set; } = "default";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal class AgentDefinitionRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Data { get; set; } = "{}";
    public string ProjectId { get; set; } = "default";
    /// <summary>"project" | "global". Visível só dentro do tenant quando "global".</summary>
    public string Visibility { get; set; } = "project";
    /// <summary>Denormalizado de projects.tenant_id; populado no upsert pelo repository.</summary>
    public string TenantId { get; set; } = "default";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ADR 0015 — regression config. NULLABLE pra rolling deploy: instâncias
    // antigas sem mapping ignoram silenciosamente.
    public string? RegressionTestSetId { get; set; }
    public string? RegressionEvaluatorConfigVersionId { get; set; }
}

internal class ProjectRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TenantId { get; set; } = "default";
    public string? Description { get; set; }
    public string Settings { get; set; } = "{}";
    public string? LlmConfig { get; set; }
    public string? Budget { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal class AgentPromptVersionRow
{
    public int RowId { get; set; }
    public string AgentId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Snapshot imutável atômico (prompt + model + tools + middlewares + schema).
internal class AgentVersionRow
{
    public string AgentVersionId { get; set; } = "";
    public string AgentDefinitionId { get; set; } = "";
    public int Revision { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ChangeReason { get; set; }
    public string Status { get; set; } = "Published";
    public string ContentHash { get; set; } = "";
    public string Snapshot { get; set; } = "{}"; // JSONB — record completo serializado
}

internal class WorkflowVersionRow
{
    public string WorkflowVersionId { get; set; } = "";
    public string WorkflowDefinitionId { get; set; } = "";
    public int Revision { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ChangeReason { get; set; }
    public string Status { get; set; } = "Published";
    public string ContentHash { get; set; } = "";
    public string Snapshot { get; set; } = "{}"; // JSONB — WorkflowDefinition serializada
}

// Skill (estado mutável) + SkillVersion (append-only imutável).
internal class SkillRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Data { get; set; } = "{}"; // JSONB
    public string ContentHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string ProjectId { get; set; } = "default";
}

internal class SkillVersionRow
{
    public string SkillVersionId { get; set; } = "";
    public string SkillId { get; set; } = "";
    public int Revision { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ChangeReason { get; set; }
    public string ContentHash { get; set; } = "";
    public string Snapshot { get; set; } = "{}"; // JSONB
}

// Background Responses (execução assíncrona de agentes).
internal class BackgroundResponseJobRow
{
    public string JobId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string? AgentVersionId { get; set; }
    public string? SessionId { get; set; }
    public string Input { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public string? Output { get; set; }
    public string? LastError { get; set; }
    public int Attempt { get; set; }
    public string? CallbackTarget { get; set; } // JSONB serializado
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

internal class WorkflowExecutionRow
{
    public string ExecutionId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string ProjectId { get; set; } = "default";
    public string Status { get; set; } = "";
    public string Data { get; set; } = "{}";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

internal class NodeExecutionRow
{
    public int RowId { get; set; }
    public string ExecutionId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string Data { get; set; } = "{}";
    public string? ProjectId { get; set; }
}

internal class AtivoRow
{
    public string Ticker { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Setor { get; set; }
    public string? Descricao { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal class LlmTokenUsageRow
{
    public long Id { get; set; }
    public string AgentId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string? ExecutionId { get; set; }
    public string? WorkflowId { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }
    public double DurationMs { get; set; }
    public string? PromptVersionId { get; set; }
    public string? AgentVersionId { get; set; }
    public string? OutputContent { get; set; }
    public int RetryCount { get; set; }
    public string? ProjectId { get; set; }
    /// <summary>
    /// Quando o agent que gerou esta entrada é cross-project (workflow caller != agent owner),
    /// guarda o ProjectId do **owner** do agent. Null quando agent é local ao caller.
    /// Permite analytics dual ("qual projeto consumiu vs qual projeto produziu").
    /// </summary>
    public string? OriginAgentProjectId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? ExperimentId { get; set; }
    public string? ExperimentVariant { get; set; }  // 'A' | 'B' | null
    // ADR 0015 — tagging cross-cutting (eval persiste source/run_id).
    public string? Metadata { get; set; }
}

internal class ModelPricingRow
{
    public int Id { get; set; }
    public string ModelId { get; set; } = "";
    public string Provider { get; set; } = "";
    public decimal PricePerInputToken { get; set; }
    public decimal PricePerOutputToken { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class DocumentIntelligencePricingRow
{
    public int Id { get; set; }
    public string ModelId { get; set; } = "";   // prebuilt-layout, prebuilt-read, ...
    public string Provider { get; set; } = "";  // AZUREAI
    public decimal PricePerPage { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class PersonaPromptTemplateRow
{
    public int Id { get; set; }
    public string Scope { get; set; } = "";
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // UpdatedBy removido da entity; coluna ainda existe no DB (EF ignora
    // colunas não mapeadas, forward-compat OK). Drop fica na
    // migration db/migration_persona_templates_drop_updatedby.sql.
    public Guid? ActiveVersionId { get; set; }
}

internal class PersonaPromptTemplateVersionRow
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public Guid VersionId { get; set; }
    public string Template { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ChangeReason { get; set; }
}

internal class PersonaPromptExperimentRow
{
    public int Id { get; set; }
    public string ProjectId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid VariantAVersionId { get; set; }
    public Guid VariantBVersionId { get; set; }
    public int TrafficSplitB { get; set; }
    public string Metric { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? CreatedBy { get; set; }
}

internal class ToolInvocationRow
{
    public long Id { get; set; }
    public string ExecutionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string? Arguments { get; set; }
    public string? Result { get; set; }
    public double DurationMs { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class WorkflowCheckpointRow
{
    public string ExecutionId { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

internal class HumanInteractionRow
{
    public string InteractionId { get; set; } = "";
    public string ExecutionId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string? Context { get; set; }
    public string InteractionType { get; set; } = "Approval";
    public string? Options { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Resolution { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    /// <summary>UserId de quem resolveu; "system:timeout" em expiração automática; NULL em Pending.</summary>
    public string? ResolvedBy { get; set; }
}

internal class AgentSessionRow
{
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string SerializedState { get; set; } = "{}";
    public int TurnCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

internal class WorkflowEventAuditRow
{
    public long Id { get; set; }
    public string ExecutionId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

// MCP Server registry (project-scoped, CRUD simples sem versions). Data é o
// McpServer serializado completo em JSONB; colunas denormalizadas (Name,
// ProjectId, CreatedAt, UpdatedAt) são só para indexar/ordenar na UI.
internal class McpServerRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Data { get; set; } = "{}";  // McpServer serializado
    public string ProjectId { get; set; } = "default";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal class AdminAuditLogRow
{
    public long Id { get; set; }
    public string? TenantId { get; set; }
    public string? ProjectId { get; set; }
    public string ActorUserId { get; set; } = "";
    public string? ActorUserType { get; set; }
    public string Action { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string? PayloadBefore { get; set; }  // JSONB mapeado como string para evitar conversão dupla.
    public string? PayloadAfter { get; set; }
    public DateTime Timestamp { get; set; }
}

// ADR 0015 — Evaluation rows. PK strings VARCHAR alinha com AgentVersionRow/
// WorkflowVersionRow. JSONB como string evita conversão dupla pelo EF.

internal class EvaluationTestSetRow
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "default";
    public string Visibility { get; set; } = "project";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CurrentVersionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

internal class EvaluationTestSetVersionRow
{
    public string TestSetVersionId { get; set; } = "";
    public string TestSetId { get; set; } = "";
    public int Revision { get; set; }
    public string Status { get; set; } = "Draft";
    public string ContentHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ChangeReason { get; set; }
}

internal class EvaluationTestCaseRow
{
    public string CaseId { get; set; } = "";
    public string TestSetVersionId { get; set; } = "";
    public int Index { get; set; }
    public string Input { get; set; } = "";
    public string? ExpectedOutput { get; set; }
    public string? ExpectedToolCalls { get; set; } // JSONB serializado como string
    public string[] Tags { get; set; } = Array.Empty<string>();
    public double Weight { get; set; } = 1.0;
    public DateTime CreatedAt { get; set; }
}

internal class EvaluatorConfigRow
{
    public string Id { get; set; } = "";
    public string AgentDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CurrentVersionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

internal class EvaluatorConfigVersionRow
{
    public string EvaluatorConfigVersionId { get; set; } = "";
    public string EvaluatorConfigId { get; set; } = "";
    public int Revision { get; set; }
    public string Status { get; set; } = "Draft";
    public string ContentHash { get; set; } = "";
    public string Bindings { get; set; } = "[]"; // JSONB array serializado
    public string Splitter { get; set; } = "LastTurn";
    public int NumRepetitions { get; set; } = 3;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ChangeReason { get; set; }
}

internal class EvaluationRunRow
{
    public string RunId { get; set; } = "";
    public string ProjectId { get; set; } = "default";
    public string AgentDefinitionId { get; set; } = "";
    public string AgentVersionId { get; set; } = "";
    public string TestSetVersionId { get; set; } = "";
    public string EvaluatorConfigVersionId { get; set; } = "";
    public string? BaselineRunId { get; set; }
    public string Status { get; set; } = "Pending";
    public int Priority { get; set; }
    public string? TriggeredBy { get; set; }
    public string TriggerSource { get; set; } = "Manual";
    public string? TriggerContext { get; set; } // JSONB serializado
    public string ExecutionId { get; set; } = "";
    public int CasesTotal { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class EvaluationRunProgressRow
{
    public string RunId { get; set; } = "";
    public int CasesCompleted { get; set; }
    public int CasesPassed { get; set; }
    public int CasesFailed { get; set; }
    public decimal? AvgScore { get; set; }
    public decimal TotalCostUsd { get; set; }
    public long TotalTokens { get; set; }
    public DateTime LastUpdated { get; set; }
}

internal class EvaluationResultRow
{
    public string ResultId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string EvaluatorName { get; set; } = "";
    public int BindingIndex { get; set; }
    public int RepetitionIndex { get; set; }
    public decimal? Score { get; set; }
    public bool Passed { get; set; }
    public string? Reason { get; set; }
    public string? OutputContent { get; set; }
    public string? JudgeModel { get; set; }
    public double? LatencyMs { get; set; }
    public decimal? CostUsd { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? EvaluatorMetadata { get; set; } // JSONB serializado
    public DateTime CreatedAt { get; set; }
}

public class AgentFwDbContext : DbContext
{
    private readonly IProjectContextAccessor? _projectAccessor;
    private readonly ITenantContextAccessor? _tenantAccessor;

    public AgentFwDbContext(
        DbContextOptions<AgentFwDbContext> options,
        IProjectContextAccessor? projectAccessor = null,
        ITenantContextAccessor? tenantAccessor = null)
        : base(options)
    {
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
    }

    /// <summary>ProjectId do scope atual. Usado pelo HasQueryFilter.</summary>
    private string CurrentProjectId => _projectAccessor?.Current.ProjectId ?? "default";

    /// <summary>TenantId do scope atual. Usado pelo HasQueryFilter pra enforçar tenant boundary
    /// em listagens cross-project (Visibility=global).</summary>
    private string CurrentTenantId => _tenantAccessor?.Current.TenantId ?? "default";

    public DbSet<ConversationSession> Conversations => Set<ConversationSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    internal DbSet<ProjectRow> Projects => Set<ProjectRow>();
    internal DbSet<WorkflowDefinitionRow> WorkflowDefinitions => Set<WorkflowDefinitionRow>();
    internal DbSet<AgentDefinitionRow> AgentDefinitions => Set<AgentDefinitionRow>();
    internal DbSet<AgentPromptVersionRow> AgentPromptVersions => Set<AgentPromptVersionRow>();
    internal DbSet<AgentVersionRow> AgentVersions => Set<AgentVersionRow>();
    internal DbSet<WorkflowVersionRow> WorkflowVersions => Set<WorkflowVersionRow>();
    internal DbSet<SkillRow> Skills => Set<SkillRow>();
    internal DbSet<SkillVersionRow> SkillVersions => Set<SkillVersionRow>();
    internal DbSet<WorkflowExecutionRow> WorkflowExecutions => Set<WorkflowExecutionRow>();
    internal DbSet<NodeExecutionRow> NodeExecutions => Set<NodeExecutionRow>();
    internal DbSet<AtivoRow> Ativos => Set<AtivoRow>();
    internal DbSet<LlmTokenUsageRow> LlmTokenUsages => Set<LlmTokenUsageRow>();
    internal DbSet<ToolInvocationRow> ToolInvocations => Set<ToolInvocationRow>();
    internal DbSet<ModelPricingRow> ModelPricings => Set<ModelPricingRow>();
    internal DbSet<DocumentIntelligencePricingRow> DocumentIntelligencePricings => Set<DocumentIntelligencePricingRow>();
    internal DbSet<PersonaPromptTemplateRow> PersonaPromptTemplates => Set<PersonaPromptTemplateRow>();
    internal DbSet<PersonaPromptTemplateVersionRow> PersonaPromptTemplateVersions => Set<PersonaPromptTemplateVersionRow>();
    internal DbSet<PersonaPromptExperimentRow> PersonaPromptExperiments => Set<PersonaPromptExperimentRow>();
    internal DbSet<WorkflowCheckpointRow> WorkflowCheckpoints => Set<WorkflowCheckpointRow>();
    internal DbSet<HumanInteractionRow> HumanInteractions => Set<HumanInteractionRow>();
    internal DbSet<AgentSessionRow> AgentSessions => Set<AgentSessionRow>();
    internal DbSet<WorkflowEventAuditRow> WorkflowEventAudits => Set<WorkflowEventAuditRow>();
    internal DbSet<AdminAuditLogRow> AdminAuditLogs => Set<AdminAuditLogRow>();
    internal DbSet<McpServerRow> McpServers => Set<McpServerRow>();
    internal DbSet<BackgroundResponseJobRow> BackgroundResponseJobs => Set<BackgroundResponseJobRow>();
    internal DbSet<EvaluationTestSetRow> EvaluationTestSets => Set<EvaluationTestSetRow>();
    internal DbSet<EvaluationTestSetVersionRow> EvaluationTestSetVersions => Set<EvaluationTestSetVersionRow>();
    internal DbSet<EvaluationTestCaseRow> EvaluationTestCases => Set<EvaluationTestCaseRow>();
    internal DbSet<EvaluatorConfigRow> EvaluatorConfigs => Set<EvaluatorConfigRow>();
    internal DbSet<EvaluatorConfigVersionRow> EvaluatorConfigVersions => Set<EvaluatorConfigVersionRow>();
    internal DbSet<EvaluationRunRow> EvaluationRuns => Set<EvaluationRunRow>();
    internal DbSet<EvaluationRunProgressRow> EvaluationRunProgress => Set<EvaluationRunProgressRow>();
    internal DbSet<EvaluationResultRow> EvaluationResults => Set<EvaluationResultRow>();

    private static JsonDocument ParseJsonDocument(string json)
        => JsonDocument.Parse(json, new JsonDocumentOptions());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("aihub");

        modelBuilder.Entity<ConversationSession>(b =>
        {
            b.ToTable("conversations");
            b.HasKey(e => e.ConversationId);
            b.Property(e => e.ConversationId).HasMaxLength(64);
            b.Property(e => e.UserId).HasMaxLength(256).IsRequired();
            b.Property(e => e.WorkflowId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Title).HasMaxLength(512);
            b.Property(e => e.ActiveExecutionId).HasMaxLength(64);
            b.Property(e => e.LastActiveAgentId).HasMaxLength(256);
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.LastMessageAt).IsRequired();

            b.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonDefaults.Domain),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonDefaults.Domain) ?? new Dictionary<string, string>(),
                    new ValueComparer<Dictionary<string, string>>(
                        (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.Count == b.Count && !a.Except(b).Any()),
                        v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
                        v => new Dictionary<string, string>(v)));

            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.HasIndex(e => e.UserId);
            b.HasIndex(e => e.LastMessageAt);
            b.HasQueryFilter(e => e.ProjectId == CurrentProjectId);
        });

        modelBuilder.Entity<ChatMessage>(b =>
        {
            b.ToTable("chat_messages");
            b.HasKey(e => e.MessageId);
            b.Property(e => e.MessageId).HasMaxLength(64);
            b.Property(e => e.ConversationId).HasMaxLength(64).IsRequired();
            b.Property(e => e.Role).HasMaxLength(32).IsRequired();
            b.Property(e => e.Content).IsRequired();
            b.Property(e => e.ExecutionId).HasMaxLength(64);
            b.Property(e => e.CreatedAt).IsRequired();

            b.Property(e => e.StructuredOutput)
                .HasColumnType("jsonb")
                .HasConversion(new ValueConverter<JsonDocument?, string?>(
                    v => v == null ? null : v.RootElement.GetRawText(),
                    v => v == null ? null : ParseJsonDocument(v)));

            // Actor armazenado como string lowercase pra ler bem em queries diretas no psql
            // e bater com o default do schema ("human"). Conversion sem `out var` porque
            // EF Core compila para expression tree que não aceita declaração de variável.
            b.Property(e => e.Actor)
                .HasColumnName("Actor")
                .HasMaxLength(32)
                .IsRequired()
                .HasDefaultValue(Actor.Human)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => string.Equals(v, "robot", StringComparison.OrdinalIgnoreCase)
                        ? Actor.Robot
                        : Actor.Human);

            b.HasIndex(e => e.ConversationId);
            b.HasIndex(e => new { e.ConversationId, e.CreatedAt });
        });

        // Colunas em lowercase para compatibilidade com PgProjectRepository (raw SQL).
        modelBuilder.Entity<ProjectRow>(b =>
        {
            b.ToTable("projects");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(128);
            b.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            b.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(128).IsRequired();
            b.Property(e => e.Description).HasColumnName("description").HasMaxLength(1024);
            b.Property(e => e.Settings).HasColumnName("settings").HasColumnType("jsonb").IsRequired();
            b.Property(e => e.LlmConfig).HasColumnName("llm_config").HasColumnType("jsonb");
            b.Property(e => e.Budget).HasColumnName("budget").HasColumnType("jsonb");
            b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
            b.HasIndex(e => e.TenantId).HasDatabaseName("ix_projects_tenant_id");
        });

        modelBuilder.Entity<WorkflowDefinitionRow>(b =>
        {
            b.ToTable("workflow_definitions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasMaxLength(256);
            b.Property(e => e.Name).HasMaxLength(512).IsRequired();
            b.Property(e => e.Data).HasColumnType("text").IsRequired();
            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.Visibility).HasMaxLength(32).HasDefaultValue("project");
            b.Property(e => e.TenantId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
            // Visibilidade: project (filtro estrito por projeto) OU global dentro do mesmo tenant.
            b.HasQueryFilter(e =>
                e.ProjectId == CurrentProjectId
                || (e.Visibility == "global" && e.TenantId == CurrentTenantId));
        });

        modelBuilder.Entity<AgentDefinitionRow>(b =>
        {
            b.ToTable("agent_definitions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasMaxLength(256);
            b.Property(e => e.Name).HasMaxLength(512).IsRequired();
            b.Property(e => e.Data).HasColumnType("text").IsRequired();
            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.Visibility).HasMaxLength(32).HasDefaultValue("project");
            b.Property(e => e.TenantId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
            b.Property(e => e.RegressionTestSetId).HasMaxLength(64);
            b.Property(e => e.RegressionEvaluatorConfigVersionId).HasMaxLength(64);
            // Tenant-aware visibility: project (estrito) OU global dentro do mesmo tenant.
            // Workflow do projeto A pode resolver agent global de B (mesmo tenant) sem
            // bypass de filter — query filter padrão já cobre.
            b.HasQueryFilter(e =>
                e.ProjectId == CurrentProjectId
                || (e.Visibility == "global" && e.TenantId == CurrentTenantId));
        });

        modelBuilder.Entity<AgentVersionRow>(b =>
        {
            b.ToTable("agent_versions");
            b.HasKey(e => e.AgentVersionId);
            b.Property(e => e.AgentVersionId).HasMaxLength(64);
            b.Property(e => e.AgentDefinitionId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Revision).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(256);
            b.Property(e => e.ChangeReason).HasMaxLength(1024);
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.ContentHash).HasMaxLength(128).IsRequired();
            b.Property(e => e.Snapshot).HasColumnType("jsonb").IsRequired();
            b.HasIndex(e => new { e.AgentDefinitionId, e.Revision }).IsUnique();
            b.HasIndex(e => e.AgentDefinitionId);
            b.HasIndex(e => e.ContentHash);
        });

        modelBuilder.Entity<WorkflowVersionRow>(b =>
        {
            b.ToTable("workflow_versions");
            b.HasKey(e => e.WorkflowVersionId);
            b.Property(e => e.WorkflowVersionId).HasMaxLength(64);
            b.Property(e => e.WorkflowDefinitionId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Revision).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(256);
            b.Property(e => e.ChangeReason).HasMaxLength(1024);
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.ContentHash).HasMaxLength(128).IsRequired();
            b.Property(e => e.Snapshot).HasColumnType("jsonb").IsRequired();
            b.HasIndex(e => new { e.WorkflowDefinitionId, e.Revision }).IsUnique();
            b.HasIndex(e => new { e.WorkflowDefinitionId, e.ContentHash }).IsUnique();
            b.HasIndex(e => e.WorkflowDefinitionId);
        });

        modelBuilder.Entity<SkillRow>(b =>
        {
            b.ToTable("skills");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasMaxLength(256);
            b.Property(e => e.Name).HasMaxLength(256).IsRequired();
            b.Property(e => e.Data).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.ContentHash).HasMaxLength(128).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.HasQueryFilter(e => e.ProjectId == CurrentProjectId);
        });

        modelBuilder.Entity<SkillVersionRow>(b =>
        {
            b.ToTable("skill_versions");
            b.HasKey(e => e.SkillVersionId);
            b.Property(e => e.SkillVersionId).HasMaxLength(64);
            b.Property(e => e.SkillId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Revision).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(256);
            b.Property(e => e.ChangeReason).HasMaxLength(1024);
            b.Property(e => e.ContentHash).HasMaxLength(128).IsRequired();
            b.Property(e => e.Snapshot).HasColumnType("jsonb").IsRequired();
            b.HasIndex(e => new { e.SkillId, e.Revision }).IsUnique();
            b.HasIndex(e => e.SkillId);
            b.HasIndex(e => e.ContentHash);
        });

        modelBuilder.Entity<AgentPromptVersionRow>(b =>
        {
            b.ToTable("agent_prompt_versions");
            b.HasKey(e => e.RowId);
            b.Property(e => e.RowId).ValueGeneratedOnAdd();
            b.Property(e => e.AgentId).HasMaxLength(256).IsRequired();
            b.Property(e => e.VersionId).HasMaxLength(128).IsRequired();
            b.Property(e => e.Content).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => new { e.AgentId, e.VersionId }).IsUnique();
            b.HasIndex(e => e.AgentId);
        });

        modelBuilder.Entity<WorkflowExecutionRow>(b =>
        {
            b.ToTable("workflow_executions");
            b.HasKey(e => e.ExecutionId);
            b.Property(e => e.ExecutionId).HasMaxLength(128);
            b.Property(e => e.WorkflowId).HasMaxLength(256).IsRequired();
            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.Data).HasColumnType("text").IsRequired();
            b.Property(e => e.StartedAt).IsRequired();
            b.HasIndex(e => e.WorkflowId);
            b.HasIndex(e => e.Status);
            b.HasIndex(e => e.StartedAt);
            b.HasIndex(e => new { e.WorkflowId, e.Status, e.StartedAt })
             .IsDescending(false, false, true)
             .HasDatabaseName("IX_workflow_executions_WorkflowId_Status_StartedAt");
            b.HasQueryFilter(e => e.ProjectId == CurrentProjectId);
        });

        modelBuilder.Entity<NodeExecutionRow>(b =>
        {
            b.ToTable("node_executions");
            b.HasKey(e => e.RowId);
            b.Property(e => e.RowId).ValueGeneratedOnAdd();
            b.Property(e => e.ExecutionId).HasMaxLength(128).IsRequired();
            b.Property(e => e.NodeId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Data).HasColumnType("text").IsRequired();
            b.Property(e => e.ProjectId).HasMaxLength(128);
            b.HasIndex(e => new { e.ExecutionId, e.NodeId }).IsUnique();
            b.HasIndex(e => e.ExecutionId);
            // Tolerância ao null em rows legadas: callers que omitem projectId
            // também passam. Remover o `|| == null` após backfill de rows
            // antigas + enforcement no trigger.
            b.HasQueryFilter(e => e.ProjectId == CurrentProjectId || e.ProjectId == null);
        });

        modelBuilder.Entity<AtivoRow>(b =>
        {
            b.ToTable("ativos");
            b.HasKey(e => e.Ticker);
            b.Property(e => e.Ticker).HasMaxLength(20);
            b.Property(e => e.Nome).HasMaxLength(255).IsRequired();
            b.Property(e => e.Setor).HasMaxLength(100);
            b.HasIndex(e => e.Setor);
        });

        modelBuilder.Entity<LlmTokenUsageRow>(b =>
        {
            b.ToTable("llm_token_usage");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.AgentId).HasMaxLength(256).IsRequired();
            b.Property(e => e.ModelId).HasMaxLength(256).IsRequired();
            b.Property(e => e.ExecutionId).HasMaxLength(128);
            b.Property(e => e.WorkflowId).HasMaxLength(256);
            b.Property(e => e.PromptVersionId).HasMaxLength(128);
            b.Property(e => e.AgentVersionId).HasMaxLength(64);
            b.Property(e => e.OutputContent).HasColumnType("text");
            b.Property(e => e.RetryCount).HasDefaultValue(0);
            b.Property(e => e.CachedTokens).HasDefaultValue(0);
            b.Property(e => e.ProjectId).HasMaxLength(128);
            b.Property(e => e.OriginAgentProjectId).HasMaxLength(128).HasColumnName("OriginAgentProjectId");
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.ExperimentId);
            b.Property(e => e.ExperimentVariant).HasMaxLength(1);
            b.Property(e => e.Metadata).HasColumnType("jsonb");
            b.HasIndex(e => e.AgentId);
            b.HasIndex(e => e.ExecutionId);
            b.HasIndex(e => e.CreatedAt);
            b.HasIndex(e => e.ExperimentId);
            // Index pra analytics dual: "qual projeto pagou X de tokens com agent global de Y".
            b.HasIndex(e => new { e.ProjectId, e.OriginAgentProjectId, e.CreatedAt })
                .HasDatabaseName("IX_llm_token_usage_caller_origin");
            // Mesma tolerância de NodeExecutionRow — rows legadas e execuções
            // sem projectId no metadata ficam com null e passam. Analytics
            // globais (GetAllAgentsSummaryAsync, GetThroughputAsync) usam SQL
            // raw e ignoram esse filter.
            b.HasQueryFilter(e => e.ProjectId == CurrentProjectId || e.ProjectId == null);
        });

        modelBuilder.Entity<ToolInvocationRow>(b =>
        {
            b.ToTable("tool_invocations");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.ExecutionId).HasMaxLength(128).IsRequired();
            b.Property(e => e.AgentId).HasMaxLength(256).IsRequired();
            b.Property(e => e.ToolName).HasMaxLength(256).IsRequired();
            b.Property(e => e.Arguments).HasColumnType("jsonb");
            b.Property(e => e.Result).HasColumnType("text");
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => e.ExecutionId);
            b.HasIndex(e => e.AgentId);
            b.HasIndex(e => e.ToolName);
        });

        modelBuilder.Entity<ModelPricingRow>(b =>
        {
            b.ToTable("model_pricing");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.ModelId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Provider).HasMaxLength(64).IsRequired();
            b.Property(e => e.PricePerInputToken).HasColumnType("numeric(20,10)").IsRequired();
            b.Property(e => e.PricePerOutputToken).HasColumnType("numeric(20,10)").IsRequired();
            b.Property(e => e.Currency).HasMaxLength(3).HasDefaultValue("USD");
            b.Property(e => e.EffectiveFrom).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => e.ModelId);
        });

        modelBuilder.Entity<DocumentIntelligencePricingRow>(b =>
        {
            b.ToTable("document_intelligence_pricing");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.ModelId).HasMaxLength(128).IsRequired();
            b.Property(e => e.Provider).HasMaxLength(64).IsRequired();
            b.Property(e => e.PricePerPage).HasColumnType("numeric(20,10)").IsRequired();
            b.Property(e => e.Currency).HasMaxLength(3).HasDefaultValue("USD");
            b.Property(e => e.EffectiveFrom).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => e.ModelId);
        });

        modelBuilder.Entity<PersonaPromptTemplateRow>(b =>
        {
            b.ToTable("persona_prompt_templates");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.Scope).HasMaxLength(128).IsRequired();
            b.Property(e => e.Name).HasMaxLength(128).IsRequired();
            b.Property(e => e.Template).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
            b.Property(e => e.ActiveVersionId);
            b.HasIndex(e => e.Scope).IsUnique();
        });

        modelBuilder.Entity<PersonaPromptTemplateVersionRow>(b =>
        {
            b.ToTable("persona_prompt_template_versions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.TemplateId).IsRequired();
            b.Property(e => e.VersionId).IsRequired();
            b.Property(e => e.Template).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(128);
            b.Property(e => e.ChangeReason).HasMaxLength(512);
            b.HasIndex(e => new { e.TemplateId, e.CreatedAt });
            b.HasIndex(e => e.VersionId).IsUnique();
        });

        // Isolamento por project é garantido pelo Repo ao filtrar por
        // ProjectId nas queries — não usamos HasQueryFilter porque o composer
        // precisa consultar experiments em contexto hot path com ProjectId
        // explícito, sem depender de AsyncLocal nesse caminho.
        modelBuilder.Entity<PersonaPromptExperimentRow>(b =>
        {
            b.ToTable("persona_prompt_experiments");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.ProjectId).HasMaxLength(128).IsRequired();
            b.Property(e => e.Scope).HasMaxLength(128).IsRequired();
            b.Property(e => e.Name).HasMaxLength(128).IsRequired();
            b.Property(e => e.VariantAVersionId).IsRequired();
            b.Property(e => e.VariantBVersionId).IsRequired();
            b.Property(e => e.TrafficSplitB).IsRequired();
            b.Property(e => e.Metric).HasMaxLength(64).IsRequired();
            b.Property(e => e.StartedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(128);
            b.HasIndex(e => new { e.ProjectId, e.StartedAt });
        });

        modelBuilder.Entity<WorkflowCheckpointRow>(b =>
        {
            b.ToTable("workflow_checkpoints");
            b.HasKey(e => e.ExecutionId);
            b.Property(e => e.ExecutionId).HasMaxLength(128);
            b.Property(e => e.Data).HasColumnType("bytea").IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<HumanInteractionRow>(b =>
        {
            b.ToTable("human_interactions");
            b.HasKey(e => e.InteractionId);
            b.Property(e => e.InteractionId).HasMaxLength(128);
            b.Property(e => e.ExecutionId).HasMaxLength(128).IsRequired();
            b.Property(e => e.WorkflowId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Prompt).IsRequired();
            b.Property(e => e.InteractionType).HasMaxLength(32).HasDefaultValue("Approval");
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.ResolvedBy).HasMaxLength(128);
            b.HasIndex(e => e.ExecutionId);
            b.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<AgentSessionRow>(b =>
        {
            b.ToTable("agent_sessions");
            b.HasKey(e => e.SessionId);
            b.Property(e => e.SessionId).HasMaxLength(128);
            b.Property(e => e.AgentId).HasMaxLength(256).IsRequired();
            b.Property(e => e.SerializedState).HasColumnType("text").IsRequired();
            b.Property(e => e.TurnCount).HasDefaultValue(0);
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.LastAccessedAt).IsRequired();
            b.Property(e => e.ExpiresAt).IsRequired();
            b.HasIndex(e => e.AgentId);
            b.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<BackgroundResponseJobRow>(b =>
        {
            b.ToTable("background_response_jobs");
            b.HasKey(e => e.JobId);
            b.Property(e => e.JobId).HasMaxLength(64);
            b.Property(e => e.AgentId).HasMaxLength(256).IsRequired();
            b.Property(e => e.AgentVersionId).HasMaxLength(64);
            b.Property(e => e.SessionId).HasMaxLength(128);
            b.Property(e => e.Input).HasColumnType("text").IsRequired();
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.Output).HasColumnType("text");
            b.Property(e => e.LastError).HasColumnType("text");
            b.Property(e => e.Attempt).HasDefaultValue(0);
            b.Property(e => e.CallbackTarget).HasColumnType("jsonb");
            b.Property(e => e.IdempotencyKey).HasMaxLength(128);
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => new { e.Status, e.CreatedAt });
            b.HasIndex(e => e.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
        });

        modelBuilder.Entity<WorkflowEventAuditRow>(b =>
        {
            b.ToTable("workflow_event_audit");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.ExecutionId).HasMaxLength(128).IsRequired();
            b.Property(e => e.EventType).HasMaxLength(64).IsRequired();
            b.Property(e => e.Payload).HasColumnType("text").IsRequired();
            b.Property(e => e.Timestamp).IsRequired();
            b.HasIndex(e => e.ExecutionId);
            b.HasIndex(e => e.Timestamp);
            b.HasIndex(e => new { e.ExecutionId, e.Id })
             .HasDatabaseName("IX_workflow_event_audit_ExecutionId_Id");
        });

        modelBuilder.Entity<McpServerRow>(b =>
        {
            b.ToTable("mcp_servers");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasMaxLength(128);
            b.Property(e => e.Name).HasMaxLength(256).IsRequired();
            b.Property(e => e.Data).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
            b.HasIndex(e => new { e.ProjectId, e.Name })
             .HasDatabaseName("IX_mcp_servers_ProjectId_Name");
            b.HasQueryFilter(e => e.ProjectId == CurrentProjectId);
        });

        // Trilha de mudanças CRUD em Project/Agent/Workflow/Skill/ModelPricing.
        // Payload* são JSONB opcionais; escritas são do raw SQL do repositório
        // (mantemos o DbSet para testes EF e consistência de schema).
        modelBuilder.Entity<AdminAuditLogRow>(b =>
        {
            b.ToTable("admin_audit_log");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.Property(e => e.TenantId).HasMaxLength(128);
            b.Property(e => e.ProjectId).HasMaxLength(128);
            b.Property(e => e.ActorUserId).HasMaxLength(128).IsRequired();
            b.Property(e => e.ActorUserType).HasMaxLength(32);
            b.Property(e => e.Action).HasMaxLength(32).IsRequired();
            b.Property(e => e.ResourceType).HasMaxLength(64).IsRequired();
            b.Property(e => e.ResourceId).HasMaxLength(128).IsRequired();
            b.Property(e => e.PayloadBefore).HasColumnType("jsonb");
            b.Property(e => e.PayloadAfter).HasColumnType("jsonb");
            b.Property(e => e.Timestamp).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.Timestamp })
             .HasDatabaseName("IX_admin_audit_log_TenantId_Timestamp")
             .IsDescending(false, true);
            b.HasIndex(e => new { e.ResourceType, e.ResourceId })
             .HasDatabaseName("IX_admin_audit_log_ResourceType_ResourceId");
            b.HasIndex(e => new { e.ActorUserId, e.Timestamp })
             .HasDatabaseName("IX_admin_audit_log_ActorUserId_Timestamp")
             .IsDescending(false, true);
        });

        modelBuilder.Entity<EvaluationTestSetRow>(b =>
        {
            b.ToTable("evaluation_test_sets");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasMaxLength(64);
            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.Visibility).HasMaxLength(32).HasDefaultValue("project");
            b.Property(e => e.Name).HasMaxLength(256).IsRequired();
            b.Property(e => e.Description).HasMaxLength(1024);
            b.Property(e => e.CurrentVersionId).HasMaxLength(64);
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(256);
            b.HasIndex(e => e.ProjectId);
            // Sem HasQueryFilter: lookup por ID exato (cross-project Copy) e
            // ops admin precisam acessar testsets de qualquer project.
            // Listagem project-scoped usa ListByProjectAsync explícito.
        });

        modelBuilder.Entity<EvaluationTestSetVersionRow>(b =>
        {
            b.ToTable("evaluation_test_set_versions");
            b.HasKey(e => e.TestSetVersionId);
            b.Property(e => e.TestSetVersionId).HasMaxLength(64);
            b.Property(e => e.TestSetId).HasMaxLength(64).IsRequired();
            b.Property(e => e.Revision).IsRequired();
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.ContentHash).HasMaxLength(128).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(256);
            b.Property(e => e.ChangeReason).HasMaxLength(1024);
            b.HasIndex(e => new { e.TestSetId, e.Revision }).IsUnique();
            b.HasIndex(e => e.TestSetId);
        });

        modelBuilder.Entity<EvaluationTestCaseRow>(b =>
        {
            b.ToTable("evaluation_test_cases");
            b.HasKey(e => e.CaseId);
            b.Property(e => e.CaseId).HasMaxLength(64);
            b.Property(e => e.TestSetVersionId).HasMaxLength(64).IsRequired();
            b.Property(e => e.Index).IsRequired();
            b.Property(e => e.Input).HasColumnType("text").IsRequired();
            b.Property(e => e.ExpectedOutput).HasColumnType("text");
            b.Property(e => e.ExpectedToolCalls).HasColumnType("jsonb");
            b.Property(e => e.Tags).HasColumnType("text[]").IsRequired();
            b.Property(e => e.Weight).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => new { e.TestSetVersionId, e.Index }).IsUnique();
            b.HasIndex(e => e.TestSetVersionId);
        });

        modelBuilder.Entity<EvaluatorConfigRow>(b =>
        {
            b.ToTable("evaluator_configs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasMaxLength(64);
            b.Property(e => e.AgentDefinitionId).HasMaxLength(256).IsRequired();
            b.Property(e => e.Name).HasMaxLength(256).IsRequired();
            b.Property(e => e.CurrentVersionId).HasMaxLength(64);
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.UpdatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(256);
            b.HasIndex(e => e.AgentDefinitionId);
        });

        modelBuilder.Entity<EvaluatorConfigVersionRow>(b =>
        {
            b.ToTable("evaluator_config_versions");
            b.HasKey(e => e.EvaluatorConfigVersionId);
            b.Property(e => e.EvaluatorConfigVersionId).HasMaxLength(64);
            b.Property(e => e.EvaluatorConfigId).HasMaxLength(64).IsRequired();
            b.Property(e => e.Revision).IsRequired();
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.ContentHash).HasMaxLength(128).IsRequired();
            b.Property(e => e.Bindings).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.Splitter).HasMaxLength(32).IsRequired();
            b.Property(e => e.NumRepetitions).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.CreatedBy).HasMaxLength(256);
            b.Property(e => e.ChangeReason).HasMaxLength(1024);
            b.HasIndex(e => new { e.EvaluatorConfigId, e.Revision }).IsUnique();
            b.HasIndex(e => e.EvaluatorConfigId);
            b.HasIndex(e => e.ContentHash);
        });

        modelBuilder.Entity<EvaluationRunRow>(b =>
        {
            b.ToTable("evaluation_runs");
            b.HasKey(e => e.RunId);
            b.Property(e => e.RunId).HasMaxLength(64);
            b.Property(e => e.ProjectId).HasMaxLength(128).HasDefaultValue("default");
            b.Property(e => e.AgentDefinitionId).HasMaxLength(256).IsRequired();
            b.Property(e => e.AgentVersionId).HasMaxLength(64).IsRequired();
            b.Property(e => e.TestSetVersionId).HasMaxLength(64).IsRequired();
            b.Property(e => e.EvaluatorConfigVersionId).HasMaxLength(64).IsRequired();
            b.Property(e => e.BaselineRunId).HasMaxLength(64);
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.Priority).IsRequired();
            b.Property(e => e.TriggeredBy).HasMaxLength(256);
            b.Property(e => e.TriggerSource).HasMaxLength(32).IsRequired();
            b.Property(e => e.TriggerContext).HasColumnType("jsonb");
            b.Property(e => e.ExecutionId).HasMaxLength(128).IsRequired();
            b.Property(e => e.CasesTotal).IsRequired();
            b.Property(e => e.LastError).HasMaxLength(2048);
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => new { e.AgentDefinitionId, e.CreatedAt })
             .IsDescending(false, true)
             .HasDatabaseName("IX_evaluation_runs_AgentDefinitionId_CreatedAt");
            b.HasIndex(e => e.ProjectId);
            b.HasQueryFilter(e => e.ProjectId == CurrentProjectId);
        });

        modelBuilder.Entity<EvaluationRunProgressRow>(b =>
        {
            b.ToTable("evaluation_run_progress");
            b.HasKey(e => e.RunId);
            b.Property(e => e.RunId).HasMaxLength(64);
            b.Property(e => e.CasesCompleted).IsRequired();
            b.Property(e => e.CasesPassed).IsRequired();
            b.Property(e => e.CasesFailed).IsRequired();
            b.Property(e => e.AvgScore).HasColumnType("numeric(5,4)");
            b.Property(e => e.TotalCostUsd).HasColumnType("numeric(12,6)").IsRequired();
            b.Property(e => e.TotalTokens).IsRequired();
            b.Property(e => e.LastUpdated).IsRequired();
        });

        modelBuilder.Entity<EvaluationResultRow>(b =>
        {
            b.ToTable("evaluation_results");
            b.HasKey(e => new { e.RunId, e.CaseId, e.EvaluatorName, e.BindingIndex, e.RepetitionIndex });
            b.Property(e => e.ResultId).HasMaxLength(64).IsRequired();
            b.Property(e => e.RunId).HasMaxLength(64).IsRequired();
            b.Property(e => e.CaseId).HasMaxLength(64).IsRequired();
            b.Property(e => e.EvaluatorName).HasMaxLength(128).IsRequired();
            b.Property(e => e.BindingIndex).IsRequired();
            b.Property(e => e.RepetitionIndex).IsRequired();
            b.Property(e => e.Score).HasColumnType("numeric(5,4)");
            b.Property(e => e.Passed).IsRequired();
            b.Property(e => e.Reason).HasColumnType("text");
            b.Property(e => e.OutputContent).HasColumnType("text");
            b.Property(e => e.JudgeModel).HasMaxLength(128);
            b.Property(e => e.LatencyMs);
            b.Property(e => e.CostUsd).HasColumnType("numeric(12,6)");
            b.Property(e => e.InputTokens);
            b.Property(e => e.OutputTokens);
            b.Property(e => e.EvaluatorMetadata).HasColumnType("jsonb");
            b.Property(e => e.CreatedAt).IsRequired();
            b.HasIndex(e => e.RunId);
            b.HasIndex(e => new { e.RunId, e.Passed });
            b.HasIndex(e => e.CaseId);
            b.HasIndex(e => e.ResultId).IsUnique();
        });
    }
}
