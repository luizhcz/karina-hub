namespace EfsAiHub.Host.Api.Models.Requests;

public record DocumentIntelligencePricingRequest
{
    public int? Id { get; init; }
    public required string ModelId { get; init; }
    public required string Provider { get; init; }
    public decimal PricePerPage { get; init; }
    public string? Currency { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
}
