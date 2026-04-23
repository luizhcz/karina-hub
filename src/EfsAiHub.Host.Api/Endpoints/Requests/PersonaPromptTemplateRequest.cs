using System.ComponentModel.DataAnnotations;

namespace EfsAiHub.Host.Api.Models.Requests;

public record PersonaPromptTemplateUpsertRequest
{
    public required string Scope { get; init; }
    public required string Name { get; init; }

    // Limite coerente com o CHECK constraint em aihub.persona_prompt_templates
    // (migration_persona_template_length.sql). Falha de validação devolve 400
    // antes de chegar no DB.
    [MaxLength(50000)]
    public required string Template { get; init; }
}

/// <summary>
/// Request do preview: renderiza template com amostra de persona sem persistir.
/// UserType decide qual amostra concreta o controller instancia (cliente vs admin).
/// Frontend envia apenas o bloco relevante; o outro pode vir null.
/// </summary>
public record PersonaPromptTemplatePreviewRequest
{
    public required string Template { get; init; }
    public required string UserType { get; init; }
    public PersonaClientPreviewSample? Client { get; init; }
    public PersonaAdminPreviewSample? Admin { get; init; }
}

public record PersonaClientPreviewSample(
    string? ClientName,
    string? SuitabilityLevel,
    string? SuitabilityDescription,
    string? BusinessSegment,
    string? Country,
    bool IsOffshore);

public record PersonaAdminPreviewSample(
    string? Username,
    string? PartnerType,
    IReadOnlyList<string>? Segments,
    IReadOnlyList<string>? Institutions,
    bool IsInternal,
    bool IsWm,
    bool IsMaster,
    bool IsBroker);
