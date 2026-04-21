namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Registro principal de cada extração no Postgres.
/// Classe mutável para permitir updates de status durante o fluxo.
/// </summary>
public class ExtractionJob
{
    public Guid Id { get; set; }
    public string ConversationId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string? SourceRef { get; set; }
    public string ContentSha256 { get; set; } = "";
    public string Model { get; set; } = "";
    public string? FeaturesHash { get; set; }
    public string Status { get; set; } = "created";
    public string? OperationId { get; set; }
    public string? ResultRef { get; set; }
    public int? PageCount { get; set; }
    public decimal? CostUsd { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int? DurationMs { get; set; }
}
