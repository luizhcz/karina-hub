namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Snapshot imutável de uma versão de <see cref="PersonaPromptTemplate"/>.
/// Append-only — editar o template cria uma linha nova em
/// <c>aihub.persona_prompt_template_versions</c> + move o ponteiro
/// <see cref="PersonaPromptTemplate.ActiveVersionId"/> pra ela.
///
/// Rollback não pula ponteiro pra versão antiga: cria uma nova version
/// copiando o conteúdo da versão alvo — o histórico fica linear e
/// auditável ("v5 foi rollback de v2").
/// </summary>
public sealed class PersonaPromptTemplateVersion
{
    public int Id { get; init; }
    public int TemplateId { get; init; }

    /// <summary>UUID estável — referenciável por <c>ActiveVersionId</c>.</summary>
    public required Guid VersionId { get; init; }

    public required string Template { get; init; }

    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ChangeReason { get; init; }
}
