namespace EfsAiHub.Core.Abstractions.Observability;

public interface IDocumentIntelligencePricingRepository
{
    Task<DocumentIntelligencePricing?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Retorna o preço vigente para (ModelId, Provider) — a linha com maior EffectiveFrom
    /// cujo EffectiveTo seja null ou futuro. Retorna null se não houver entrada ativa.
    /// </summary>
    Task<DocumentIntelligencePricing?> GetCurrentAsync(string modelId, string provider, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentIntelligencePricing>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentIntelligencePricing>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<DocumentIntelligencePricing> UpsertAsync(DocumentIntelligencePricing pricing, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
