namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Preço de um modelo LLM por token (input e output) em um período de vigência.
/// </summary>
public class ModelPricing
{
    public int Id { get; set; }
    public required string ModelId { get; init; }
    public required string Provider { get; init; }
    public decimal PricePerInputToken { get; set; }
    public decimal PricePerOutputToken { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
