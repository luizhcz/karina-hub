using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.Trading;

/// <summary>
/// Output estruturado do agente de relatório de posição.
/// </summary>
[Description("Output da resposta do agente de relatório de posição do cliente.")]
public class OutputRelatorio
{
    [Description("Mensagem de resposta ao usuário com resumo textual das posições.")]
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [Description("Lista de posições do cliente obtidas via get_asset_position.")]
    [JsonPropertyName("posicoes")]
    public required List<PosicaoCliente> Posicoes { get; set; }

    [Description("Componente de UI a ser exibido (ex: position_report, position_empty).")]
    [JsonPropertyName("ui_component")]
    public required string UiComponent { get; set; }
}

/// <summary>
/// Posição de um ativo específico na carteira do cliente.
/// </summary>
[Description("Posição do cliente em um ativo.")]
public class PosicaoCliente
{
    [Description("Ticker do ativo (ex: PETR4).")]
    [JsonPropertyName("ticker")]
    public required string Ticker { get; set; }

    [Description("Quantidade total de ativos na posição.")]
    [JsonPropertyName("totalQuantity")]
    public required double TotalQuantity { get; set; }

    [Description("Volume financeiro total da posição em BRL.")]
    [JsonPropertyName("financialVolume")]
    public required double FinancialVolume { get; set; }
}
