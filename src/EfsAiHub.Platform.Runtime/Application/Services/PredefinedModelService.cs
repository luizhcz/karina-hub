using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.PredefinedModels;
using EfsAiHub.Platform.Runtime.Interfaces;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class PredefinedModelService : IPredefinedModelService
{
    private readonly IPredefinedModelRepository _repo;
    private readonly ILogger<PredefinedModelService> _logger;

    public PredefinedModelService(
        IPredefinedModelRepository repo,
        ILogger<PredefinedModelService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<PredefinedModel> CreateAsync(PredefinedModel model, CancellationToken ct = default)
    {
        var preset = new PredefinedModel
        {
            Id = (model.Id ?? string.Empty).Trim(),
            DisplayName = (model.DisplayName ?? string.Empty).Trim(),
            Description = model.Description ?? string.Empty,
            Provider = (model.Provider ?? string.Empty).Trim(),
            ClientType = string.IsNullOrWhiteSpace(model.ClientType) ? null : model.ClientType.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(model.Endpoint) ? null : model.Endpoint.Trim(),
            DeploymentName = (model.DeploymentName ?? string.Empty).Trim(),
            DefaultTemperature = model.DefaultTemperature,
            DefaultMaxTokens = model.DefaultMaxTokens,
            Enabled = model.Enabled,
        };

        preset.EnsureInvariants();

        if (await _repo.GetByIdAsync(preset.Id, ct) is not null)
            throw new PredefinedModelIdConflictException(preset.Id);

        var saved = await _repo.CreateAsync(preset, ct);
        _logger.LogInformation("[PredefinedModelService] Preset '{Id}' criado.", saved.Id);
        return saved;
    }

    public Task<PredefinedModel?> GetAsync(string id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<PredefinedModel>> ListAsync(bool includeDisabled = false, CancellationToken ct = default)
        => _repo.ListAsync(includeDisabled, ct);

    public async Task<PredefinedModel> UpdateAsync(
        string id,
        PredefinedModel patch,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"PredefinedModel '{id}' não encontrado.");

        var updated = new PredefinedModel
        {
            Id = existing.Id,
            DisplayName = (patch.DisplayName ?? string.Empty).Trim(),
            Description = patch.Description ?? string.Empty,
            Provider = (patch.Provider ?? string.Empty).Trim(),
            ClientType = string.IsNullOrWhiteSpace(patch.ClientType) ? null : patch.ClientType.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(patch.Endpoint) ? null : patch.Endpoint.Trim(),
            DeploymentName = (patch.DeploymentName ?? string.Empty).Trim(),
            DefaultTemperature = patch.DefaultTemperature,
            DefaultMaxTokens = patch.DefaultMaxTokens,
            Enabled = patch.Enabled,
            CreatedAt = existing.CreatedAt,
        };

        updated.EnsureInvariants();

        return await _repo.UpdateAsync(updated, expectedUpdatedAt, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null)
            throw new KeyNotFoundException($"PredefinedModel '{id}' não encontrado.");

        await _repo.DeleteAsync(id, ct);
        _logger.LogInformation("[PredefinedModelService] Preset '{Id}' removido.", id);
    }
}
