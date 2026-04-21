using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.Trading;

/// <summary>
/// Output estruturado do agente de boletas.
/// </summary>
[Description("Output da resposta do agente de boleta.")]
public class OutputBoleta
{
    [Description("Lista de boletas geradas pelo agente.")]
    [JsonPropertyName("boletas")]
    public required List<Boleta> BoletaList { get; set; }

    [Description("Mensagem de resposta ao usuário.")]
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [Description("Comando a ser executado pela UI.")]
    [JsonPropertyName("command")]
    public required string Command { get; set; }

    [Description("Componente de UI a ser exibido.")]
    [JsonPropertyName("ui_component")]
    public required string UiComponent { get; set; }
}

/// <summary>
/// Boleta (ordem) gerada pelo agente transacional.
/// </summary>
[Description("Boleta gerada pelo agente.")]
public class Boleta
{
    [Description("Tipo da ordem: Buy ou Sell.")]
    [JsonPropertyName("order_type")]
    public required string OrderType { get; set; }

    [Description("Ticker do ativo (ex.: PETR4).")]
    [JsonPropertyName("ticker")]
    public required string Ticker { get; set; }

    [Description("Conta do cliente.")]
    [JsonPropertyName("account")]
    public required string Account { get; set; }

    [Description("Quantidade de ativos na ordem.")]
    [JsonPropertyName("quantity")]
    public required string Quantity { get; set; }

    [Description("Preço limite da ordem.")]
    [JsonPropertyName("priceLimit")]
    public required string PriceLimit { get; set; }

    [Description("Tipo de preço: M (mercado), L (limitado), F (financeiro).")]
    [JsonPropertyName("priceType")]
    public required string PriceType { get; set; }

    [Description("Volume financeiro da ordem em BRL.")]
    [JsonPropertyName("volume")]
    public required string Volume { get; set; }

    [Description("Vencimento da boleta (data normalizada ou vazio).")]
    [JsonPropertyName("expireTime")]
    public required string ExpireTime { get; set; }
}
