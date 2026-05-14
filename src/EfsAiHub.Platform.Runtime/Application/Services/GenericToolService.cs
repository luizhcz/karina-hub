using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class GenericToolService : IGenericToolService
{
    private readonly IGenericToolRepository _repo;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IOptions<GenericToolsOptions> _options;
    private readonly ILogger<GenericToolService> _logger;

    public GenericToolService(
        IGenericToolRepository repo,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        IOptions<GenericToolsOptions> options,
        ILogger<GenericToolService> logger)
    {
        _repo = repo;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _options = options;
        _logger = logger;
    }

    public async Task<GenericTool> CreateAsync(string? id, GenericTool draft, CancellationToken ct = default)
    {
        var projectId = _projectAccessor.Current.ProjectId;
        var tenantId = _tenantAccessor.Current.TenantId;
        var toolId = !string.IsNullOrWhiteSpace(id) ? id!.Trim() : Guid.NewGuid().ToString("N");

        var tool = new GenericTool
        {
            Id = toolId,
            ProjectId = projectId,
            TenantId = tenantId,
            Name = draft.Name?.Trim() ?? string.Empty,
            Description = draft.Description ?? string.Empty,
            HttpMethod = draft.HttpMethod,
            UrlTemplate = draft.UrlTemplate?.Trim() ?? string.Empty,
            PathParams = draft.PathParams,
            QueryParams = draft.QueryParams,
            CustomHeaders = draft.CustomHeaders,
            InputContentType = NormalizeInputContentType(draft.HttpMethod, draft.InputContentType),
            InputSchema = draft.HttpMethod == HttpMethodType.GET ? null : draft.InputSchema,
            OutputContentType = draft.OutputContentType,
            OutputSchema = draft.OutputContentType == OutputContentType.Text ? null : draft.OutputSchema,
            TimeoutSecondsOverride = draft.TimeoutSecondsOverride,
            WhenToUse = draft.WhenToUse,
            IsExclusive = draft.IsExclusive,
        };

        ValidateAll(tool);

        if (await _repo.NameExistsAsync(tool.Name, excludeId: null, ct))
            throw new GenericToolNameConflictException(tool.Name);

        var saved = await _repo.CreateAsync(tool, ct);

        _logger.LogInformation(
            "[GenericToolService] Tool '{ToolId}' criada em projeto '{ProjectId}'.",
            saved.Id, projectId);

        return saved;
    }

    public Task<GenericTool?> GetAsync(string id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<GenericTool>> ListAsync(CancellationToken ct = default)
        => _repo.ListAsync(ct);

    public async Task<GenericTool> UpdateAsync(
        string id,
        GenericTool patch,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"GenericTool '{id}' não encontrado.");

        var updated = new GenericTool
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            TenantId = existing.TenantId,
            Name = patch.Name?.Trim() ?? string.Empty,
            Description = patch.Description ?? string.Empty,
            HttpMethod = patch.HttpMethod,
            UrlTemplate = patch.UrlTemplate?.Trim() ?? string.Empty,
            PathParams = patch.PathParams,
            QueryParams = patch.QueryParams,
            CustomHeaders = patch.CustomHeaders,
            InputContentType = NormalizeInputContentType(patch.HttpMethod, patch.InputContentType),
            InputSchema = patch.HttpMethod == HttpMethodType.GET ? null : patch.InputSchema,
            OutputContentType = patch.OutputContentType,
            OutputSchema = patch.OutputContentType == OutputContentType.Text ? null : patch.OutputSchema,
            TimeoutSecondsOverride = patch.TimeoutSecondsOverride,
            WhenToUse = patch.WhenToUse,
            IsExclusive = patch.IsExclusive,
            CreatedAt = existing.CreatedAt,
        };

        ValidateAll(updated);

        if (!string.Equals(existing.Name, updated.Name, StringComparison.Ordinal)
            && await _repo.NameExistsAsync(updated.Name, excludeId: id, ct))
            throw new GenericToolNameConflictException(updated.Name);

        return await _repo.UpdateAsync(updated, expectedUpdatedAt, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null)
            throw new KeyNotFoundException($"GenericTool '{id}' não encontrado.");

        await _repo.DeleteAsync(id, ct);

        _logger.LogInformation(
            "[GenericToolService] Tool '{ToolId}' descartada (projeto '{ProjectId}').",
            id, existing.ProjectId);
    }

    private void ValidateAll(GenericTool tool)
    {
        tool.EnsureInvariants();
        tool.EnsureWithinTimeoutCeiling(_options.Value.MaxTimeoutSeconds);
    }

    private static InputContentType NormalizeInputContentType(HttpMethodType method, InputContentType requested)
        => method == HttpMethodType.GET ? InputContentType.None : requested;
}
