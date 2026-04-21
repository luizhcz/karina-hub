namespace EfsAiHub.Core.Abstractions.Projects;

/// <summary>
/// Modelo LLM disponível em um provider específico.
/// </summary>
public sealed class ModelCatalog
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public int? ContextWindow { get; set; }
    /// <summary>Lista de capacidades, ex: ["chat","vision","function_calling"].</summary>
    public List<string> Capabilities { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public interface IModelCatalogRepository
{
    Task<IReadOnlyList<ModelCatalog>> GetAllAsync(string? provider = null, bool activeOnly = true, CancellationToken ct = default);
    Task<IReadOnlyList<ModelCatalog>> GetAllAsync(string? provider, bool activeOnly, int page, int pageSize, CancellationToken ct = default);
    Task<int> CountAsync(string? provider = null, bool activeOnly = true, CancellationToken ct = default);
    Task<ModelCatalog?> GetByIdAsync(string id, string provider, CancellationToken ct = default);
    Task<ModelCatalog> UpsertAsync(ModelCatalog model, CancellationToken ct = default);
    Task<bool> SetActiveAsync(string id, string provider, bool isActive, CancellationToken ct = default);
}
