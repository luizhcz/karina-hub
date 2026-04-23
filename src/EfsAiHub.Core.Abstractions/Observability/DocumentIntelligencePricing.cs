namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Preço de um modelo do Azure Document Intelligence por página. Diferente do
/// <see cref="ModelPricing"/> (LLM) que é por token — DI cobra por página processada.
/// </summary>
public class DocumentIntelligencePricing
{
    public int Id { get; set; }
    public required string ModelId { get; init; }  // prebuilt-layout, prebuilt-read, etc.
    public required string Provider { get; init; } // AZUREAI (único hoje)
    public decimal PricePerPage { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
