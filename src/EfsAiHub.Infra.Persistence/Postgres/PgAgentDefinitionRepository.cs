using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgAgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly IEfsRedisCache _cache;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IAgentPromptRepository _promptRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ISkillVersionRepository? _skillVersionRepo;
    private readonly IFunctionToolRegistry? _functionRegistry;
    private readonly IAdminAuditLogger? _auditLogger;
    private readonly ILogger<PgAgentDefinitionRepository> _logger;
    private readonly TimeSpan _ttl;

    // Cache tenant-aware: efs-ai-hub:agent-def:{tenantId}:{id}. Tenant A nunca compartilha
    // slot com tenant B — se algum caminho bypass query filter, cache não vaza.
    private const string CacheKeyPrefix = "agent-def:";
    private string CacheKey(string id) => $"{CacheKeyPrefix}{_tenantAccessor.Current.TenantId}:{id}";

    public PgAgentDefinitionRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        IEfsRedisCache cache,
        IAgentVersionRepository versionRepo,
        IAgentPromptRepository promptRepo,
        IProjectRepository projectRepo,
        ITenantContextAccessor tenantAccessor,
        ILogger<PgAgentDefinitionRepository> logger,
        IConfiguration config,
        ISkillVersionRepository? skillVersionRepo = null,
        IFunctionToolRegistry? functionRegistry = null,
        IAdminAuditLogger? auditLogger = null)
    {
        _factory = factory;
        _cache = cache;
        _versionRepo = versionRepo;
        _promptRepo = promptRepo;
        _projectRepo = projectRepo;
        _tenantAccessor = tenantAccessor;
        _skillVersionRepo = skillVersionRepo;
        _functionRegistry = functionRegistry;
        _auditLogger = auditLogger;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(config.GetValue<int>("Redis:DefinitionCacheTtlSeconds", 300));
    }

    /// <summary>
    /// Hidrata Visibility/ProjectId/TenantId da row sobre o domain — defesa contra JSON
    /// legado (sem esses campos) ou inconsistência transient. Row é source of truth.
    /// LastChatSandboxValidated* vivem APENAS nas colunas (não no jsonb do Data,
    /// graças ao JsonIgnore no model) — hidratação aqui é obrigatória pra que
    /// callers vejam o gate.
    /// </summary>
    private static AgentDefinition Hydrate(AgentDefinitionRow row, AgentDefinition def)
    {
        def.ProjectId = row.ProjectId;
        def.TenantId = row.TenantId;
        def.Visibility = row.Visibility;
        def.LastChatSandboxValidatedAt = row.LastChatSandboxValidatedAt;
        def.LastChatSandboxValidatedByUserId = row.LastChatSandboxValidatedByUserId;
        def.LastChatSandboxValidatedAgentVersionId = row.LastChatSandboxValidatedAgentVersionId;
        return def;
    }

    public async Task<AgentDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(id);

        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
            return DeserializeFromCacheEnvelope(cached);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // FirstOrDefaultAsync respeita HasQueryFilter (project OR global+tenant) — caller
        // de outro tenant não vê. Nunca usar FindAsync aqui (bypass de filter).
        var row = await ctx.AgentDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return null;

        await _cache.SetStringAsync(cacheKey, SerializeCacheEnvelope(row), _ttl);
        var def = JsonSerializer.Deserialize<AgentDefinition>(row.Data, JsonDefaults.Domain)!;
        return Hydrate(row, def);
    }

    /// <summary>
    /// Envelope cache: <c>{ data: <jsonb-do-agent>, validation: { at, byUserId, versionId } }</c>.
    /// Colunas <c>LastChatSandboxValidated*</c> não viajam no <c>row.Data</c> (têm
    /// <see cref="System.Text.Json.Serialization.JsonIgnoreAttribute"/>) então cache hit
    /// que só armazena o Data perde o gate de validation. Envelope explícito preserva.
    /// </summary>
    private static string SerializeCacheEnvelope(AgentDefinitionRow row)
    {
        var envelope = new CacheEnvelope(
            row.Data,
            row.LastChatSandboxValidatedAt,
            row.LastChatSandboxValidatedByUserId,
            row.LastChatSandboxValidatedAgentVersionId);
        return JsonSerializer.Serialize(envelope, JsonDefaults.Domain);
    }

    private static AgentDefinition? DeserializeFromCacheEnvelope(string cached)
    {
        // BC: cache pré-envelope tinha row.Data direto como jsonb-do-agent. Detecta
        // pelo formato e cai pro path antigo (definition vai ter validation=null,
        // mesma semântica que tinha antes desta mudança — não regressão).
        AgentDefinition? def;
        try
        {
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(cached, JsonDefaults.Domain);
            if (envelope?.Data is null)
                def = JsonSerializer.Deserialize<AgentDefinition>(cached, JsonDefaults.Domain);
            else
            {
                def = JsonSerializer.Deserialize<AgentDefinition>(envelope.Data, JsonDefaults.Domain);
                if (def is not null)
                {
                    def.LastChatSandboxValidatedAt = envelope.LastChatSandboxValidatedAt;
                    def.LastChatSandboxValidatedByUserId = envelope.LastChatSandboxValidatedByUserId;
                    def.LastChatSandboxValidatedAgentVersionId = envelope.LastChatSandboxValidatedAgentVersionId;
                }
            }
        }
        catch (JsonException)
        {
            def = JsonSerializer.Deserialize<AgentDefinition>(cached, JsonDefaults.Domain);
        }
        return def;
    }

    private sealed record CacheEnvelope(
        string Data,
        DateTime? LastChatSandboxValidatedAt,
        string? LastChatSandboxValidatedByUserId,
        string? LastChatSandboxValidatedAgentVersionId);

    public async Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // ToListAsync respeita HasQueryFilter — retorna agents do projeto atual + globais do tenant.
        var rows = await ctx.AgentDefinitions.ToListAsync(ct);
        return rows
            .Select(r => Hydrate(r, JsonSerializer.Deserialize<AgentDefinition>(r.Data, JsonDefaults.Domain)!))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AgentDefinition> UpsertAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool breakingChange = false,
        string? changeReason = null,
        string? createdBy = null,
        bool isCosmeticOnly = false)
    {
        // Carimba FingerprintHash em cada function tool com o hash canônico
        // (sha256 de name|description|jsonSchema) do AIFunction atualmente registrado.
        // Snapshots subsequentes de AgentVersion herdam esse hash via AgentVersion.FromDefinition.
        definition = StampFunctionFingerprints(definition);

        // Lookup do tenant do project owner — denormaliza pra row pra que o
        // query filter no DbContext consiga enforçar tenant boundary sem JOIN.
        var ownerProject = await _projectRepo.GetByIdAsync(definition.ProjectId, ct);
        var tenantId = ownerProject?.TenantId ?? "default";
        // Sincroniza no domain pra que serializações futuras carreguem o valor correto.
        definition.TenantId = tenantId;

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(definition, JsonDefaults.Domain);
        var now = DateTime.UtcNow;

        // IgnoreQueryFilters: precisamos achar a row mesmo quando o caller não enxerga
        // o agent pelo filter atual (raro — apenas com bypass owner-gated upstream).
        var existing = await ctx.AgentDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == definition.Id, ct);
        var visibilityChanged = existing is not null && existing.Visibility != definition.Visibility;

        if (existing is null)
        {
            ctx.AgentDefinitions.Add(new AgentDefinitionRow
            {
                Id = definition.Id,
                Name = definition.Name,
                Data = data,
                ProjectId = definition.ProjectId,
                Visibility = definition.Visibility,
                TenantId = tenantId,
                CreatedAt = now,
                UpdatedAt = now,
                // ADR 0015 — propaga as 3 colunas regression. Sem isso o
                // AgentRegressionConfigController.Update salvaria silenciosamente
                // só o Data JSON e o autotrigger nunca dispararia.
                RegressionTestSetId = definition.RegressionTestSetId,
                RegressionEvaluatorConfigVersionId = definition.RegressionEvaluatorConfigVersionId
            });
        }
        else
        {
            existing.Name = definition.Name;
            existing.Data = data;
            existing.Visibility = definition.Visibility;
            existing.TenantId = tenantId;
            existing.UpdatedAt = now;
            existing.RegressionTestSetId = definition.RegressionTestSetId;
            existing.RegressionEvaluatorConfigVersionId = definition.RegressionEvaluatorConfigVersionId;
        }

        await ctx.SaveChangesAsync(ct);

        // Cache tenant-aware: invalida total em mudança de visibility (outros projetos
        // do tenant podem estar com cache stale); senão atualiza só se já existia.
        // Envelope inclui LastChatSandboxValidated* (não viajam no jsonb por JsonIgnore).
        if (visibilityChanged)
        {
            await _cache.RemoveAsync(CacheKey(definition.Id));
        }
        else
        {
            // existing tem a versão pós-update (mesma EF entity tracking); novo Insert
            // grava sem validation cols (null) — o cleanup do create-time não trafega.
            var rowForCache = existing ?? new AgentDefinitionRow
            {
                Data = data,
                LastChatSandboxValidatedAt = null,
                LastChatSandboxValidatedByUserId = null,
                LastChatSandboxValidatedAgentVersionId = null,
            };
            await _cache.SetIfExistsAsync(CacheKey(definition.Id), SerializeCacheEnvelope(rowForCache), _ttl);
        }

        // Dual-write: append de snapshot imutável atômico.
        // Idempotente por ContentHash: upserts consecutivos sem mudança real não criam nova revision.
        try
        {
            (string Content, string VersionId)? prompt = null;
            try { prompt = await _promptRepo.GetActivePromptWithVersionAsync(definition.Id, ct); }
            catch { /* best-effort — prompt ainda pode não existir no momento do upsert inicial */ }

            // Fallback: agents que não usam o PromptRepo têm o texto direto em
            // definition.Instructions. Sem esse fallback, ContentHash ignoraria
            // mudanças de instructions e AppendAsync no-opa em revisões diferentes
            // — efeito visível: aprovação não gera nova AgentVersion.
            var promptContent = prompt?.Content ?? definition.Instructions;

            var revision = await _versionRepo.GetNextRevisionAsync(definition.Id, ct);

            // Materializa SkillVersionId concreto para cada SkillRef;
            // garante que rollback resolva a versão exata da skill em uso no momento do publish.
            IReadOnlyList<SkillRef>? materializedSkills = null;
            if (_skillVersionRepo is not null && definition.SkillRefs.Count > 0)
            {
                var resolved = new List<SkillRef>(definition.SkillRefs.Count);
                foreach (var r in definition.SkillRefs)
                {
                    if (!string.IsNullOrEmpty(r.SkillVersionId))
                    {
                        resolved.Add(r);
                        continue;
                    }
                    try
                    {
                        var current = await _skillVersionRepo.GetCurrentAsync(r.SkillId, ct);
                        resolved.Add(current is null ? r : new SkillRef(r.SkillId, current.SkillVersionId));
                    }
                    catch
                    {
                        resolved.Add(r); // best-effort
                    }
                }
                materializedSkills = resolved;
            }

            var snapshot = AgentVersion.FromDefinition(
                definition,
                revision,
                promptContent: promptContent,
                promptVersionId: prompt?.VersionId,
                createdBy: createdBy,
                changeReason: changeReason,
                skillRefs: materializedSkills,
                breakingChange: breakingChange);

            var persisted = await _versionRepo.AppendAsync(snapshot, ct);

            // AppendAsync é idempotente por ContentHash. Nova revision criada
            // (revision retornada bate com a que solicitamos) significa que o
            // agent mudou e o gate "validated for chat" precisa zerar — testes
            // anteriores não valem mais. Conferência por revision evita falsos
            // positivos quando AppendAsync retorna a row pré-existente.
            // Cosmetic-only edits (description/metadata) preservam validation:
            // o LLM responde igual, testes anteriores continuam representativos.
            // O classifier de tier roda em AgentDraftService e propaga via param.
            if (persisted.Revision == revision && !isCosmeticOnly)
            {
                var previousValidatedVersionId = await ClearChatSandboxValidationAsync(definition.Id, ct);
                if (previousValidatedVersionId is not null)
                    await TryEmitValidationInvalidatedAudit(
                        definition, persisted.AgentVersionId, previousValidatedVersionId, createdBy, ct);
            }
        }
        catch (Exception ex)
        {
            // Não bloqueia a escrita primária — logamos para retomada manual/backfill.
            _logger.LogWarning(ex,
                "[PgAgentDefinitionRepository] Falha ao gravar AgentVersion snapshot para '{AgentId}'.",
                definition.Id);
        }

        return definition;
    }

    private async Task TryEmitValidationInvalidatedAudit(
        AgentDefinition definition,
        string newAgentVersionId,
        string previousValidatedAgentVersionId,
        string? createdBy,
        CancellationToken ct)
    {
        if (_auditLogger is null) return;
        try
        {
            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                agentId = definition.Id,
                previousValidatedAgentVersionId,
                newAgentVersionId,
            }, JsonDefaults.Domain));
            await _auditLogger.RecordAsync(new AdminAuditEntry
            {
                TenantId = _tenantAccessor.Current.TenantId,
                ProjectId = definition.ProjectId,
                ActorUserId = createdBy ?? "system:agent-publish",
                ActorUserType = createdBy is null ? AdminAuditActorTypes.System : AdminAuditActorTypes.Human,
                Action = AdminAuditActions.ChatSandboxValidationInvalidated,
                ResourceType = AdminAuditResources.ChatSandboxSession,
                ResourceId = definition.Id,
                PayloadAfter = payload,
                Timestamp = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            // Audit não bloqueia o publish — só registra warning.
            _logger.LogWarning(ex,
                "[PgAgentDefinitionRepository] Falha ao emitir audit ChatSandboxValidationInvalidated pra '{AgentId}'.",
                definition.Id);
        }
    }

    public async Task<bool> SetChatSandboxValidationAsync(
        string agentId,
        DateTime validatedAt,
        string validatedByUserId,
        string validatedAgentVersionId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // IgnoreQueryFilters: precisamos achar a row mesmo em paths cross-project
        // (validation cross-project é decisão da feature). Mas defesa em
        // profundidade: tenant boundary é absoluto. Reject se o agent é de
        // outro tenant — caller não deveria conseguir validar nada fora do
        // próprio tenant boundary, mesmo com bypass de query filter.
        var row = await ctx.AgentDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == agentId, ct);
        if (row is null) return false;

        var currentTenantId = _tenantAccessor.Current.TenantId;
        if (!string.Equals(row.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agente '{agentId}' pertence a outro tenant. Cross-tenant validation não é permitida.");

        row.LastChatSandboxValidatedAt = validatedAt;
        row.LastChatSandboxValidatedByUserId = validatedByUserId;
        row.LastChatSandboxValidatedAgentVersionId = validatedAgentVersionId;
        await ctx.SaveChangesAsync(ct);

        // Cache do envelope contém as 3 colunas — invalida pra forçar re-cache
        // com a validação fresca no próximo GetByIdAsync.
        await _cache.RemoveAsync(CacheKey(agentId));
        return true;
    }

    public async Task<string?> ClearChatSandboxValidationAsync(string agentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == agentId, ct);
        if (row is null) return null;

        var currentTenantId = _tenantAccessor.Current.TenantId;
        if (!string.Equals(row.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Agente '{agentId}' pertence a outro tenant. Cross-tenant clear não é permitido.");

        var previousValidatedVersionId = row.LastChatSandboxValidatedAgentVersionId;
        if (previousValidatedVersionId is null
            && row.LastChatSandboxValidatedAt is null
            && row.LastChatSandboxValidatedByUserId is null)
        {
            return null; // já estava limpo
        }

        row.LastChatSandboxValidatedAt = null;
        row.LastChatSandboxValidatedByUserId = null;
        row.LastChatSandboxValidatedAgentVersionId = null;
        await ctx.SaveChangesAsync(ct);

        await _cache.RemoveAsync(CacheKey(agentId));
        return previousValidatedVersionId;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // Respeita filter — caller só consegue deletar agents visíveis ao project/tenant atual.
        var row = await ctx.AgentDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.AgentDefinitions.Remove(row);
        await ctx.SaveChangesAsync(ct);

        await _cache.RemoveAsync(CacheKey(id));
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.AgentDefinitions.AnyAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Reconstrói <see cref="AgentDefinition"/> com o <c>FingerprintHash</c>
    /// atual de cada function tool no registry. Tools desconhecidas ou sem registry
    /// preservam o hash existente (ou null).
    /// </summary>
    private AgentDefinition StampFunctionFingerprints(AgentDefinition definition)
    {
        if (_functionRegistry is null || definition.Tools.Count == 0) return definition;

        var changed = false;
        var rebuilt = new List<AgentToolDefinition>(definition.Tools.Count);
        foreach (var tool in definition.Tools)
        {
            if (!string.Equals(tool.Type, "function", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(tool.Name))
            {
                rebuilt.Add(tool);
                continue;
            }

            var latest = _functionRegistry.GetLatestFingerprint(tool.Name!);
            if (latest is null || latest == tool.FingerprintHash)
            {
                rebuilt.Add(tool);
                continue;
            }

            changed = true;
            rebuilt.Add(new AgentToolDefinition
            {
                Type = tool.Type,
                Name = tool.Name,
                RequiresApproval = tool.RequiresApproval,
                FingerprintHash = latest,
                ServerLabel = tool.ServerLabel,
                ServerUrl = tool.ServerUrl,
                AllowedTools = tool.AllowedTools,
                RequireApproval = tool.RequireApproval,
                Headers = tool.Headers,
                ConnectionId = tool.ConnectionId
            });
        }

        if (!changed) return definition;

        return new AgentDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Model = definition.Model,
            Provider = definition.Provider,
            AuthorInstructions = definition.AuthorInstructions,
            Instructions = definition.Instructions,
            Tools = rebuilt,
            StructuredOutput = definition.StructuredOutput,
            Middlewares = definition.Middlewares,
            Resilience = definition.Resilience,
            CostBudget = definition.CostBudget,
            SkillRefs = definition.SkillRefs,
            Metadata = definition.Metadata,
            CreatedAt = definition.CreatedAt,
            UpdatedAt = definition.UpdatedAt
        };
    }

    public async Task<IReadOnlySet<string>> GetExistingIdsAsync(
        IEnumerable<string> ids, CancellationToken ct = default)
    {
        var arr = ids.ToArray();
        if (arr.Length == 0) return new HashSet<string>();
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var found = await ctx.AgentDefinitions
            .Where(r => arr.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct);
        return found.ToHashSet();
    }

    public async Task<IReadOnlyList<string>> ListAgentIdsUsingGenericToolAsync(
        string genericToolId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(genericToolId)) return Array.Empty<string>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Bypass de query filter: propagator/delete-block rodam fora de HTTP
        // (sem ProjectContext válido); precisamos varrer agentes cross-project
        // pra capturar todos os referenciadores. jsonb_path_exists evita
        // deserialização full no DB e usa o índice GIN sobre Data quando
        // disponível.
        var results = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id"
            FROM aihub.agent_definitions
            WHERE jsonb_path_exists(
                "Data"::jsonb,
                '$.Tools[*] ? (@.GenericToolId == $tid)',
                jsonb_build_object('tid', @tid)
            )
            ORDER BY "Id"
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "@tid";
        p.Value = genericToolId;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(reader.GetString(0));
        return results;
    }

    public async Task<IReadOnlyList<string>> ListAgentIdsUsingPredefinedModelAsync(
        string predefinedModelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(predefinedModelId)) return Array.Empty<string>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var results = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id"
            FROM aihub.agent_definitions
            WHERE "Data"::jsonb #>> '{Model,PredefinedModelId}' = @mid
            ORDER BY "Id"
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "@mid";
        p.Value = predefinedModelId;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(reader.GetString(0));
        return results;
    }

    public async Task<IReadOnlyList<(string AgentId, string MissingProjectId)>> ListOrphanGlobalAgentsAsync(
        int limit = 20, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // IgnoreQueryFilters: health check roda fora de scope HTTP (sem project/tenant válido).
        // Read-only, idempotente. LEFT JOIN seria mais limpo mas projects é internal — usamos
        // sub-query Any() inverso.
        var orphans = await ctx.AgentDefinitions
            .IgnoreQueryFilters()
            .Where(a => a.Visibility == "global"
                && !ctx.Projects.IgnoreQueryFilters().Any(p => p.Id == a.ProjectId))
            .OrderBy(a => a.Id)
            .Take(Math.Max(1, limit))
            .Select(a => new { a.Id, a.ProjectId })
            .ToListAsync(ct);

        return orphans.Select(o => (o.Id, o.ProjectId)).ToList();
    }
}
