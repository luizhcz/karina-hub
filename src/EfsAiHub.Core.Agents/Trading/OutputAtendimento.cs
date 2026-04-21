using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.Trading;

/// <summary>
/// Output unificado (discriminated union) para o agente de atendimento.
/// O campo ResponseType discrimina entre boleta, relatório e texto livre.
/// </summary>
[Description("Output unificado do agente de atendimento. Use response_type para discriminar o tipo de resposta.")]
public class OutputAtendimento
{
    [Description("Tipo de resposta: 'boleta' para ordens, 'relatorio' para posições, 'texto' para mensagens livres/fora de escopo.")]
    [JsonPropertyName("response_type")]
    public required string ResponseType { get; set; }

    [Description("Lista de boletas geradas (presente quando response_type='boleta').")]
    [JsonPropertyName("boletas")]
    public List<Boleta>? Boletas { get; set; }

    [Description("Comando a ser executado pela UI (presente quando response_type='boleta').")]
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [Description("Lista de posições do cliente (presente quando response_type='relatorio').")]
    [JsonPropertyName("posicoes")]
    public List<PosicaoCliente>? Posicoes { get; set; }

    [Description("Mensagem de resposta ao usuário.")]
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [Description("Componente de UI a ser exibido.")]
    [JsonPropertyName("ui_component")]
    public required string UiComponent { get; set; }
}
