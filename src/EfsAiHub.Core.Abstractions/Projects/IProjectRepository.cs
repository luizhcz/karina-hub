namespace EfsAiHub.Core.Abstractions.Projects;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(string projectId, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> GetByTenantAsync(string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default);
    Task CreateAsync(Project project, CancellationToken ct = default);
    Task UpdateAsync(Project project, CancellationToken ct = default);
    Task DeleteAsync(string projectId, CancellationToken ct = default);
}
