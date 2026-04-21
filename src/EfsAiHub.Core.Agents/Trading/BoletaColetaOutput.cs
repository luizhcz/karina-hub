using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.Trading;

/// <summary>
/// Output estruturado do agente coletor de boletas.
/// Usado no primeiro nó do workflow iterativo — o agente preenche este objeto
/// com o que conseguiu extrair do histórico da conversa e sinaliza se está
/// pronto para passar ao nó executor.
/// </summary>
[Description("Output do agente coletor de campos de boleta.")]
public class BoletaColetaOutput
{
    /// <summary>
    /// true quando todos os campos obrigatórios foram coletados e o workflow
    /// pode avançar para o nó executor. false = ainda faltam dados.
    /// O roteamento do grafo usa substring "\"pronto\":true" para decidir.
    /// </summary>
    [Description("true se todos os campos obrigatórios foram coletados; false se ainda faltam dados.")]
    [JsonPropertyName("pronto")]
    public required bool Pronto { get; set; }

    /// <summary>
    /// Mensagem a ser exibida ao usuário. Quando pronto=false, é uma pergunta
    /// sobre o campo faltante. Quando pronto=true, é uma confirmação do que
    /// será executado antes de passar para o nó executor.
    /// </summary>
    [Description("Pergunta ao usuário sobre campo faltante, ou confirmação quando pronto=true.")]
    [JsonPropertyName("mensagem_usuario")]
    public required string MensagemUsuario { get; set; }

    /// <summary>
    /// Campos coletados até o momento. Campos não informados ficam null —
    /// o agente preenche progressivamente a cada turn do chat.
    /// </summary>
    [Description("Campos extraídos da conversa até o momento.")]
    [JsonPropertyName("campos_coletados")]
    public required BoletaCampos CamposColetados { get; set; }
}

/// <summary>
/// Campos de uma boleta, extraídos progressivamente da conversa.
/// </summary>
[Description("Campos de uma boleta extraídos da conversa.")]
public class BoletaCampos
{
    /// <summary>"compra" | "venda"</summary>
    [Description("Tipo de operação: 'compra' ou 'venda'.")]
    [JsonPropertyName("tipo_operacao")]
    public string? TipoOperacao { get; set; }

    /// <summary>Ticker canônico, ex: "PETR4"</summary>
    [Description("Ticker canônico do ativo, ex: 'PETR4'.")]
    [JsonPropertyName("ticker")]
    public string? Ticker { get; set; }

    /// <summary>Quantidade em unidades (inteiro positivo)</summary>
    [Description("Quantidade de unidades da ordem (inteiro positivo).")]
    [JsonPropertyName("quantidade")]
    public int? Quantidade { get; set; }

    /// <summary>
    /// Valor financeiro em BRL. Pode ser preço por unidade ou volume total,
    /// dependendo do tipo_preco escolhido.
    /// </summary>
    [Description("Valor financeiro em BRL — preço unitário (L) ou volume total (F).")]
    [JsonPropertyName("valor_brl")]
    public decimal? ValorBrl { get; set; }

    /// <summary>"M" (mercado) | "L" (limitado) | "F" (financeiro). Default: "M".</summary>
    [Description("Tipo de preço: 'M' (mercado), 'L' (limitado) ou 'F' (financeiro). Default: 'M'.")]
    [JsonPropertyName("tipo_preco")]
    public string? TipoPreco { get; set; }

    /// <summary>
    /// Campos que ainda precisam ser informados pelo usuário.
    /// Ex: ["ticker", "quantidade"]. Vazio quando pronto=true.
    /// </summary>
    [Description("Lista de campos obrigatórios ainda não informados.")]
    [JsonPropertyName("campos_faltantes")]
    public List<string> CamposFaltantes { get; set; } = [];
}
