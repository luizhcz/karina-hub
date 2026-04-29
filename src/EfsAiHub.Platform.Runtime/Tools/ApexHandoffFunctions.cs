using System.ComponentModel;
using System.Text.Json;

namespace EfsAiHub.Platform.Runtime.Tools;

/// <summary>
/// Tools para os agentes do workflow de atendimento Apex Capital (Handoff mode).
/// Implementações mock para o case de estudo — retornam dados simulados.
/// </summary>
public static class ApexHandoffFunctions
{
    // ── get_portfolio ─────────────────────────────────────────────────────────

    [Description("Consulta a carteira completa do cliente. Retorna posição de todos os ativos com valor financeiro e variação no ano.")]
    public static Task<string> GetPortfolio(
        [Description("Número da conta do cliente. Se não souber, use 'demo'.")]
        string conta = "demo")
    {
        var portfolio = new
        {
            conta,
            consultadoEm = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            patrimonioTotal = 3_000_000.00,
            retornoAno = 0.087,
            ativos = new object[]
            {
                new { ticker = "TESOURO IPCA+ 2029", tipo = "Renda Fixa",  valorFinanceiro = 850_000.00, percentualCarteira = 0.283, retornoAno =  0.112 },
                new { ticker = "CDB Bradesco 120% CDI", tipo = "Renda Fixa", valorFinanceiro = 525_000.00, percentualCarteira = 0.175, retornoAno = 0.141 },
                new { ticker = "KNCR11",  tipo = "FII",    valorFinanceiro = 420_000.00, percentualCarteira = 0.140, retornoAno =  0.097 },
                new { ticker = "HGLG11",  tipo = "FII",    valorFinanceiro = 380_000.00, percentualCarteira = 0.127, retornoAno =  0.089 },
                new { ticker = "VALE3",   tipo = "Ações",  valorFinanceiro = 310_000.00, percentualCarteira = 0.103, retornoAno = -0.042 },
                new { ticker = "PETR4",   tipo = "Ações",  valorFinanceiro = 275_000.00, percentualCarteira = 0.092, retornoAno =  0.068 },
                new { ticker = "ITUB4",   tipo = "Ações",  valorFinanceiro = 240_000.00, percentualCarteira = 0.080, retornoAno =  0.134 },
            }
        };

        return Task.FromResult(JsonSerializer.Serialize(portfolio));
    }

    // ── redeem_asset ──────────────────────────────────────────────────────────

    [Description("Executa resgate de ativo financeiro. REQUER APROVAÇÃO DO CLIENTE antes de executar. Retorna confirmação com número de referência e prazo de liquidação.")]
    public static Task<string> RedeemAsset(
        [Description("Número da conta do cliente")]
        string conta,
        [Description("Nome ou ticker do ativo a resgatar (ex: 'TESOURO IPCA+ 2029', 'KNCR11', 'CDB Bradesco 120% CDI')")]
        string ativo,
        [Description("Valor em reais a resgatar, sem formatação (ex: '100000' para R$100.000)")]
        string valor)
    {
        var refId = Guid.NewGuid().ToString("N")[..8].ToUpper();
        var resultado = new
        {
            status = "executado",
            refId,
            conta,
            ativo,
            valorResgatado = $"R$ {valor}",
            liquidacao = "D+1 útil",
            mensagem = $"Resgate de R${valor} em {ativo} registrado com sucesso. Ref: {refId}. Crédito em D+1 útil."
        };
        return Task.FromResult(JsonSerializer.Serialize(resultado));
    }

    // ── invest_asset ──────────────────────────────────────────────────────────

    [Description("Executa aplicação em ativo financeiro. REQUER APROVAÇÃO DO CLIENTE antes de executar. Retorna confirmação com número de referência e prazo de liquidação.")]
    public static Task<string> InvestAsset(
        [Description("Número da conta do cliente")]
        string conta,
        [Description("Nome ou ticker do ativo alvo (ex: 'TESOURO SELIC 2029', 'KNCR11', 'CDB Bradesco 120% CDI')")]
        string ativo,
        [Description("Valor em reais a aplicar, sem formatação (ex: '50000' para R$50.000)")]
        string valor)
    {
        var refId = Guid.NewGuid().ToString("N")[..8].ToUpper();
        var resultado = new
        {
            status = "executado",
            refId,
            conta,
            ativo,
            valorAplicado = $"R$ {valor}",
            liquidacao = "D+0",
            mensagem = $"Aplicação de R${valor} em {ativo} registrada com sucesso. Ref: {refId}. Liquidação em D+0."
        };
        return Task.FromResult(JsonSerializer.Serialize(resultado));
    }

    // ── calculate_asset_redemption_tax ────────────────────────────────────────

    [Description("Calcula o IR estimado de um resgate de renda fixa pela tabela regressiva (Lei 11.033/2004). Retorna alíquota, IR em reais e valor líquido estimado.")]
    public static Task<string> CalculateAssetRedemptionTax(
        [Description("Tipo ou nome do ativo (ex: 'Tesouro IPCA+', 'CDB', 'LCI', 'LCA', 'Debênture Incentivada')")]
        string ativo,
        [Description("Prazo da aplicação em dias corridos (ex: 730 para 2 anos)")]
        int prazoEmDias,
        [Description("Valor bruto do rendimento a resgatar em reais (ex: 100000)")]
        decimal valorBruto)
    {
        var ativoUpper = ativo.ToUpperInvariant();

        // LCI, LCA e debêntures incentivadas são isentas de IR para PF
        if (ativoUpper.Contains("LCI") || ativoUpper.Contains("LCA") ||
            ativoUpper.Contains("DEBENTURE INCENTIVADA") || ativoUpper.Contains("DEBÊNTURE INCENTIVADA"))
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                ativo,
                prazoEmDias,
                valorBruto = (double)valorBruto,
                isento = true,
                aliquota = 0.0,
                irEstimado = 0.0,
                valorLiquido = (double)valorBruto,
                fundamento = "Isento de IR para pessoa física (Lei 11.033/2004)"
            }));
        }

        // Tabela regressiva de renda fixa
        (double aliquota, string faixa) = prazoEmDias switch
        {
            <= 180 => (0.225, "até 180 dias"),
            <= 360 => (0.200, "181 a 360 dias"),
            <= 720 => (0.175, "361 a 720 dias"),
            _ => (0.150, "acima de 720 dias")
        };

        var ir = Math.Round((double)valorBruto * aliquota, 2);
        var liquido = Math.Round((double)valorBruto - ir, 2);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            ativo,
            prazoEmDias,
            valorBruto = (double)valorBruto,
            faixaTributaria = faixa,
            aliquota,
            irEstimado = ir,
            valorLiquido = liquido,
            fundamento = $"Tabela regressiva IR — {aliquota * 100:F1}% para {faixa} (Lei 11.033/2004, Art. 1°)"
        }));
    }
}
