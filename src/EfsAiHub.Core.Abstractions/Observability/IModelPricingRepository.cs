namespace EfsAiHub.Core.Abstractions.Observability;

public interface IModelPricingRepository
{
    Task<ModelPricing?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<ModelPricing> UpsertAsync(ModelPricing pricing, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
