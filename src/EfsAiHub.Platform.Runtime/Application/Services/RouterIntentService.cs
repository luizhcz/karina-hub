using System.Text.Json;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Platform.Runtime.Interfaces;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class RouterIntentService : IRouterIntentService
{
    private const string AnalyzerWorkflowId = "wf-router-intent-analyzer";

    private readonly IRouterIntentRepository _repo;
    private readonly IAgentRouterIntentLinkRepository _linkRepo;
    private readonly IWorkflowService _workflowService;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<RouterIntentService> _logger;

    public RouterIntentService(
        IRouterIntentRepository repo,
        IAgentRouterIntentLinkRepository linkRepo,
        IWorkflowService workflowService,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        ILogger<RouterIntentService> logger)
    {
        _repo = repo;
        _linkRepo = linkRepo;
        _workflowService = workflowService;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    public async Task<RouterIntent> CreateAsync(string? id, RouterIntent draft, CancellationToken ct = default)
    {
        var tenantId = _tenantAccessor.Current.TenantId;
        var intentId = !string.IsNullOrWhiteSpace(id) ? id!.Trim() : Guid.NewGuid().ToString("N");

        // ProjectId default = projeto do contexto. Form do MVP omite o campo
        // (decisão de produto: categoria = projeto atual). Caller administrativo
        // ainda pode forçar via payload.
        var projectId = string.IsNullOrWhiteSpace(draft.ProjectId)
            ? _projectAccessor.Current.ProjectId
            : draft.ProjectId.Trim();

        // Name canônico (snake_case) é decidido pelo analyzer. Quando o caller
        // não envia (form do MVP omite), derivamos do DisplayName como fallback
        // simples: lowercase ASCII + trocas de espaços por underscore.
        var displayName = (draft.DisplayName ?? string.Empty).Trim();
        var name = !string.IsNullOrWhiteSpace(draft.Name)
            ? draft.Name!.Trim()
            : SlugifyDisplayName(displayName);

        var intent = new RouterIntent
        {
            Id = intentId,
            TenantId = tenantId,
            ProjectId = projectId,
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            Description = (draft.Description ?? string.Empty).Trim(),
            Examples = draft.Examples?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList()
                       ?? (IReadOnlyList<string>)Array.Empty<string>(),
        };

        EnsureValid(intent);

        if (await _repo.NameExistsAsync(intent.Name, excludeId: null, ct))
            throw new RouterIntentNameConflictException(intent.Name);

        var saved = await _repo.CreateAsync(intent, ct);

        _logger.LogInformation(
            "[RouterIntentService] Intent '{IntentId}' (Name='{Name}') criada no tenant '{TenantId}', categoria='{ProjectId}'.",
            saved.Id, saved.Name, tenantId, saved.ProjectId);

        return saved;
    }

    public Task<RouterIntent?> GetAsync(string id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<RouterIntent>> ListAsync(CancellationToken ct = default)
        => _repo.ListAsync(ct);

    public async Task<RouterIntent> UpdateAsync(string id, RouterIntent patch, CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"RouterIntent '{id}' não encontrada.");

        if (!string.IsNullOrWhiteSpace(patch.ProjectId))
            existing.ProjectId = patch.ProjectId.Trim();

        var displayName = (patch.DisplayName ?? string.Empty).Trim();
        existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;

        // Name canônico só muda se caller mandou explicitamente; se não veio,
        // preserva — analyzer pode ter sugerido valor que o user aceitou no
        // create e não mudou no edit.
        if (!string.IsNullOrWhiteSpace(patch.Name))
            existing.Name = patch.Name.Trim();
        else if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(displayName))
            existing.Name = SlugifyDisplayName(displayName);

        existing.Description = (patch.Description ?? string.Empty).Trim();
        existing.Examples = patch.Examples?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList()
                            ?? (IReadOnlyList<string>)Array.Empty<string>();

        EnsureValid(existing);

        if (await _repo.NameExistsAsync(existing.Name, excludeId: id, ct))
            throw new RouterIntentNameConflictException(existing.Name);

        var saved = await _repo.UpdateAsync(existing, ct);

        _logger.LogInformation(
            "[RouterIntentService] Intent '{IntentId}' atualizada (Name='{Name}', categoria='{ProjectId}').",
            saved.Id, saved.Name, saved.ProjectId);

        return saved;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var usage = await _linkRepo.ListAgentsForIntentAsync(id, ct);
        if (usage.Count > 0)
            throw new RouterIntentInUseException(id, usage.Select(u => u.AgentId).ToList());

        var deleted = await _repo.DeleteAsync(id, ct);
        if (deleted)
            _logger.LogInformation("[RouterIntentService] Intent '{IntentId}' removida.", id);
        return deleted;
    }

    public Task<IReadOnlyList<RouterIntentUsage>> GetUsageAsync(string id, CancellationToken ct = default)
        => _linkRepo.ListAgentsForIntentAsync(id, ct);

    public async Task<string> AnalyzeAsync(AnalyzeRouterIntentInput input, CancellationToken ct = default)
    {
        var existingAll = await _repo.ListAsync(ct);
        var existingFiltered = string.IsNullOrEmpty(input.ExcludeId)
            ? existingAll
            : existingAll.Where(e => e.Id != input.ExcludeId).ToList();

        var payload = new
        {
            existing = existingFiltered.Select(e => new
            {
                name = e.Name,
                displayName = e.DisplayName,
                description = e.Description,
                projectId = e.ProjectId,
            }),
            candidate = new
            {
                description = input.Description,
                examples = input.Examples,
                nameHint = input.NameHint,
                displayNameHint = input.DisplayNameHint,
                projectIdHint = input.ProjectIdHint,
            },
        };

        var inputJson = JsonSerializer.Serialize(payload, JsonDefaults.Domain);

        // Workflow vive no projeto 'geral' com Visibility=global; HasQueryFilter
        // do WorkflowDefinitionRow já cobre cross-project nesse caso.
        var executionId = await _workflowService.TriggerAsync(
            workflowId: AnalyzerWorkflowId,
            inputPayload: inputJson,
            metadata: new Dictionary<string, string>
            {
                ["source"] = "router-intent-analyzer",
                ["tenantId"] = _tenantAccessor.Current.TenantId,
                ["projectId"] = _projectAccessor.Current.ProjectId,
            },
            source: ExecutionSource.Api,
            mode: ExecutionMode.Production,
            workflowVersionId: null,
            ct: ct);

        _logger.LogInformation(
            "[RouterIntentService] Analyze disparado executionId={ExecutionId} (excludeId={ExcludeId}).",
            executionId, input.ExcludeId ?? "null");

        return executionId;
    }

    private static void EnsureValid(RouterIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.ProjectId))
            throw new ArgumentException("RouterIntent.ProjectId é obrigatório (referência a um projeto do tenant).");
        if (string.IsNullOrWhiteSpace(intent.Name))
            throw new ArgumentException("RouterIntent.Name é obrigatório.");
        if (string.IsNullOrWhiteSpace(intent.Description))
            throw new ArgumentException("RouterIntent.Description é obrigatório.");
    }

    /// <summary>
    /// Slug fallback quando o caller não envia <c>Name</c>. Usado só no MVP
    /// quando o user salvou sem rodar o analyzer (fluxo "salvar sem analisar")
    /// e o LLM não opinou. Em fluxo normal, o analyzer já retorna <c>Name</c>
    /// canônico em snake_case ASCII.
    /// </summary>
    private static string SlugifyDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
        var normalized = displayName.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized.Normalize(System.Text.NormalizationForm.FormD))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('_');
        }
        var slug = sb.ToString();
        while (slug.Contains("__", StringComparison.Ordinal))
            slug = slug.Replace("__", "_");
        return slug.Trim('_');
    }
}
