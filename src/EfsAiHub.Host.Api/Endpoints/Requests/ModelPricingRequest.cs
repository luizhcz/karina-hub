namespace EfsAiHub.Host.Api.Models.Requests;

public record ModelPricingRequest
{
    public int? Id { get; init; }
    public required string ModelId { get; init; }
    public required string Provider { get; init; }
    public decimal PricePerInputToken { get; init; }
    public decimal PricePerOutputToken { get; init; }
    public string? Currency { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
}
