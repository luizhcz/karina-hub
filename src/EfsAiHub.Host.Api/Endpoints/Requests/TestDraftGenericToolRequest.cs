using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do POST /api/aihub/generic-tools/test-draft. Permite testar uma
/// configuração de tool ANTES dela ser persistida — UI usa pra forçar o
/// PM a validar a ferramenta funcionando contra o endpoint real antes
/// de habilitar o botão "Criar". Sem audit, sem métricas, sem write.
/// </summary>
public sealed class TestDraftGenericToolRequest
{
    [Required]
    public CreateGenericToolRequest Tool { get; init; } = new();

    public Dictionary<string, JsonElement> Args { get; init; } = new();
}
