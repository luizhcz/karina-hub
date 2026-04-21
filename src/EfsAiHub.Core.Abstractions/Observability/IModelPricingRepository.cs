namespace EfsAiHub.Core.Abstractions.Observability;

public interface IModelPricingRepository
{
    Task<ModelPricing?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ModelPricing>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<ModelPricing> UpsertAsync(ModelPricing pricing, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Reconstrói a materialized view <c>v_llm_cost</c> a partir dos dados atuais de token usage
    /// e pricing. Usa <c>REFRESH MATERIALIZED VIEW CONCURRENTLY</c> — não bloqueia leitura.
    /// </summary>
    Task RefreshMaterializedViewAsync(CancellationToken ct = default);
}
