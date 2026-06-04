using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Agents.Services;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Platform.Runtime.Tools.Generic.Schema;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class GenericToolService : IGenericToolService
{
    private readonly IGenericToolRepository _repo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentDependencyPropagator _propagator;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IOptions<GenericToolsOptions> _options;
    private readonly ISchemaNormalizer _normalizer;
    private readonly ILogger<GenericToolService> _logger;

    public GenericToolService(
        IGenericToolRepository repo,
        IAgentDefinitionRepository agentRepo,
        IAgentDependencyPropagator propagator,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        IOptions<GenericToolsOptions> options,
        ISchemaNormalizer normalizer,
        ILogger<GenericToolService> logger)
    {
        _repo = repo;
        _agentRepo = agentRepo;
        _propagator = propagator;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _options = options;
        _normalizer = normalizer;
        _logger = logger;
    }

    public async Task<GenericToolSaveResult> CreateAsync(string? id, GenericTool draft, CancellationToken ct = default)
    {
        var projectId = _projectAccessor.Current.ProjectId;
        var tenantId = _tenantAccessor.Current.TenantId;
        var toolId = !string.IsNullOrWhiteSpace(id) ? id!.Trim() : Guid.NewGuid().ToString("N");

        var inputContentType = NormalizeInputContentType(draft.HttpMethod, draft.InputContentType);
        var (inputCanonical, inputWarnings) = NormalizeSchema(
            inputContentType is InputContentType.None or InputContentType.Text ? null : draft.InputSchema,
            SchemaRole.Input);
        var (outputCanonical, outputWarnings) = NormalizeSchema(
            draft.OutputContentType == OutputContentType.Text ? null : draft.OutputSchema,
            SchemaRole.Output);

        var tool = new GenericTool
        {
            Id = toolId,
            ProjectId = projectId,
            TenantId = tenantId,
            Name = draft.Name?.Trim() ?? string.Empty,
            HttpMethod = draft.HttpMethod,
            UrlTemplate = draft.UrlTemplate?.Trim() ?? string.Empty,
            PathParams = draft.PathParams,
            QueryParams = draft.QueryParams,
            CustomHeaders = draft.CustomHeaders,
            InputContentType = inputContentType,
            InputSchema = inputCanonical,
            OutputContentType = draft.OutputContentType,
            OutputSchema = outputCanonical,
            OutputProjectionMode = ResolveProjectionMode(draft.OutputContentType),
            TimeoutSecondsOverride = draft.TimeoutSecondsOverride,
            IsExclusive = draft.IsExclusive,
        };

        ValidateAll(tool);

        if (await _repo.NameExistsAsync(tool.Name, excludeId: null, ct))
            throw new GenericToolNameConflictException(tool.Name);

        var saved = await _repo.CreateAsync(tool, ct);

        _logger.LogInformation(
            "[GenericToolService] Tool '{ToolId}' criada em projeto '{ProjectId}' (warnings={WarningCount}).",
            saved.Id, projectId, inputWarnings.Count + outputWarnings.Count);

        return new GenericToolSaveResult(saved, Combine(inputWarnings, outputWarnings));
    }

    public Task<GenericTool?> GetAsync(string id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<GenericTool>> ListAsync(CancellationToken ct = default)
        => _repo.ListAsync(ct);

    public async Task<GenericToolSaveResult> UpdateAsync(
        string id,
        GenericTool patch,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"GenericTool '{id}' não encontrado.");

        var inputContentType = NormalizeInputContentType(patch.HttpMethod, patch.InputContentType);
        var (inputCanonical, inputWarnings) = NormalizeSchema(
            inputContentType is InputContentType.None or InputContentType.Text ? null : patch.InputSchema,
            SchemaRole.Input);
        var (outputCanonical, outputWarnings) = NormalizeSchema(
            patch.OutputContentType == OutputContentType.Text ? null : patch.OutputSchema,
            SchemaRole.Output);

        var updated = new GenericTool
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            TenantId = existing.TenantId,
            Name = patch.Name?.Trim() ?? string.Empty,
            HttpMethod = patch.HttpMethod,
            UrlTemplate = patch.UrlTemplate?.Trim() ?? string.Empty,
            PathParams = patch.PathParams,
            QueryParams = patch.QueryParams,
            CustomHeaders = patch.CustomHeaders,
            InputContentType = inputContentType,
            InputSchema = inputCanonical,
            OutputContentType = patch.OutputContentType,
            OutputSchema = outputCanonical,
            OutputProjectionMode = ResolveProjectionMode(patch.OutputContentType),
            TimeoutSecondsOverride = patch.TimeoutSecondsOverride,
            IsExclusive = patch.IsExclusive,
            CreatedAt = existing.CreatedAt,
        };

        ValidateAll(updated);

        if (!string.Equals(existing.Name, updated.Name, StringComparison.Ordinal)
            && await _repo.NameExistsAsync(updated.Name, excludeId: id, ct))
            throw new GenericToolNameConflictException(updated.Name);

        var saved = await _repo.UpdateAsync(updated, expectedUpdatedAt, ct);
        await _propagator.PropagateGenericToolEditAsync(saved.Id, ct);
        return new GenericToolSaveResult(saved, Combine(inputWarnings, outputWarnings));
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null)
            throw new KeyNotFoundException($"GenericTool '{id}' não encontrado.");

        // Bloqueio análogo ao RouterIntent: snapshot precisa carregar os
        // campos inline da tool; deletar enquanto agentes referenciam quebra
        // o composer no próximo edit. UI mostra a lista pra remover refs.
        var users = await _agentRepo.ListAgentIdsUsingGenericToolAsync(id, ct);
        if (users.Count > 0)
            throw new GenericToolInUseException(id, users);

        await _repo.DeleteAsync(id, ct);

        _logger.LogInformation(
            "[GenericToolService] Tool '{ToolId}' descartada (projeto '{ProjectId}').",
            id, existing.ProjectId);
    }

    /// <summary>
    /// Roda o normalizer pra obter o JSON canônico. Quando a entrada é
    /// null/whitespace, retorna null sem warning — schema ausente é estado
    /// legítimo (ex.: InputContentType=None ou OutputContentType=Text).
    /// </summary>
    private (string? Canonical, IReadOnlyList<NormalizationWarning> Warnings) NormalizeSchema(
        string? raw,
        SchemaRole role)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, Array.Empty<NormalizationWarning>());

        var result = _normalizer.Normalize(raw, role);
        return (result.CanonicalJson, result.Warnings);
    }

    private static IReadOnlyList<NormalizationWarning> Combine(
        IReadOnlyList<NormalizationWarning> a,
        IReadOnlyList<NormalizationWarning> b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var merged = new List<NormalizationWarning>(a.Count + b.Count);
        merged.AddRange(a);
        merged.AddRange(b);
        return merged;
    }

    private void ValidateAll(GenericTool tool)
    {
        tool.EnsureInvariants();
        tool.EnsureWithinTimeoutCeiling(_options.Value.MaxTimeoutSeconds);
    }

    private static InputContentType NormalizeInputContentType(HttpMethodType method, InputContentType requested)
    {
        // GET aceita None ou Json (input estruturado vira query string).
        // FormUrlEncoded/Text em GET não fazem sentido — normaliza pra None
        // pra falhar visível em EnsureInvariants se ainda mandar schema.
        if (method == HttpMethodType.GET
            && requested is InputContentType.FormUrlEncoded or InputContentType.Text)
        {
            return InputContentType.None;
        }
        return requested;
    }

    /// <summary>
    /// Json/Csv sempre projetam (drop silencioso de extras + fail-loud em
    /// required/type). Text fica Off — texto puro não tem shape pra projetar.
    /// Derivado do OutputContentType porque o cliente não tem mais select de
    /// modo no form (V1 do MVP).
    /// </summary>
    private static OutputProjectionMode ResolveProjectionMode(OutputContentType outputType)
        => outputType == OutputContentType.Text
            ? OutputProjectionMode.Off
            : OutputProjectionMode.Project;
}
