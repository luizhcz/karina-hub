using System.ComponentModel.DataAnnotations;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do PUT /api/aihub/agents/{id}. Idêntico ao <see cref="CreateAgentRequest"/>
/// mas com <c>ChangeReason</c> OBRIGATÓRIO — toda atualização de agent já
/// publicado precisa de justificativa (vai pro audit em
/// <c>agent_approval_history</c> como <c>AdminOverride</c> + Feedback). Manter
/// como tipo separado torna o contrato explícito no controller e na geração
/// do OpenAPI.
/// </summary>
public sealed class UpdateAgentRequest : CreateAgentRequest
{
    [Required(ErrorMessage = "ChangeReason é obrigatório no PUT — descreva a motivação da mudança (vai pro audit).")]
    [MinLength(10, ErrorMessage = "ChangeReason precisa ter pelo menos 10 caracteres.")]
    [MaxLength(2048)]
    public new required string ChangeReason { get; init; } = string.Empty;
}
