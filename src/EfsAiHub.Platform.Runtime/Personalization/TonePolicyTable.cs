namespace EfsAiHub.Platform.Runtime.Personalization;

/// <summary>
/// Lookup estático (Segment × RiskProfile) → tone_policy para LLM.
///
/// Tabela hardcoded é decisão consciente: compliance financeira exige
/// auditabilidade do texto exato que orienta recomendações ao cliente —
/// 1 arquivo C# reviewable em PR bate um YAML ou tabela de DB.
///
/// Evolução (backlog PERSONA-2): introduzir <c>ITonePolicyResolver</c> com
/// impl default encapsulando esta tabela, pra troca sem modificar a classe.
/// </summary>
internal static class TonePolicyTable
{
    private static readonly Dictionary<(string Segment, string Risk), string> _table =
        new(new TupleEqualityComparer())
        {
            // private
            [("private", "conservador")] =
                "Formal e técnico. Sugestões somente de renda fixa grau de investimento e fundos conservadores. " +
                "Evitar linguagem agressiva ou sugestões de alavancagem. " +
                "Assumir que o cliente valoriza preservação de capital sobre retorno.",
            [("private", "moderado")] =
                "Formal e técnico. Mix de renda fixa e renda variável blue-chip. " +
                "Linguagem balanceada entre preservação e crescimento. Permitido sugerir alocação em multimercados.",
            [("private", "agressivo")] =
                "Formal e técnico. Todo espectro de produtos elegível, incluindo derivativos e alavancagem controlada. " +
                "Tom analítico sobre risco-retorno sem excessos de cautela.",

            // institucional
            [("institucional", "conservador")] =
                "Formal, técnico e denso. Foco em renda fixa institucional, LCIs/LCAs/CDBs de grandes bancos, debêntures AAA. " +
                "Respostas podem incluir detalhes tributários e de marcação a mercado.",
            [("institucional", "moderado")] =
                "Formal, técnico e denso. Renda fixa + fundos multimercados institucionais. " +
                "Pode discutir yield, duration e correlação entre classes.",
            [("institucional", "agressivo")] =
                "Formal, técnico e denso. Todos produtos elegíveis incluindo estruturados (COE), opções e operações de balcão.",

            // corporativo
            [("corporativo", "conservador")] =
                "Formal e prático. Produtos de caixa corporativo: CDB, compromissadas, títulos públicos. " +
                "Evitar jargão acadêmico; foco em preservação de capital de giro.",
            [("corporativo", "moderado")] =
                "Formal e prático. Mix entre caixa e investimento: CDB, fundos DI e multimercados com baixa volatilidade.",
            [("corporativo", "agressivo")] =
                "Formal e prático. Inclui produtos estruturados para tesouraria (swaps, NDF, opções de hedge) e investimento em ações.",

            // varejo
            [("varejo", "conservador")] =
                "Linguagem acessível, evitar jargão. Renda fixa e fundos D+0/D+1 priorizados. " +
                "Sempre explicar riscos e liquidez em termos simples.",
            [("varejo", "moderado")] =
                "Linguagem acessível, com exemplos quando apropriado. Renda fixa e renda variável blue-chip. " +
                "Sempre explicar o trade-off risco-retorno.",
            [("varejo", "agressivo")] =
                "Linguagem acessível porém direta. Todo espectro elegível incluindo ações de menor capitalização. " +
                "Reforçar educação de risco em cada sugestão.",
        };

    public static string? Lookup(string? segment, string? riskProfile)
    {
        if (string.IsNullOrWhiteSpace(segment) || string.IsNullOrWhiteSpace(riskProfile))
            return null;

        var seg = segment.Trim().ToLowerInvariant();
        var risk = riskProfile.Trim().ToLowerInvariant();
        return _table.TryGetValue((seg, risk), out var policy) ? policy : null;
    }

    private sealed class TupleEqualityComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.Ordinal)
            && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

        public int GetHashCode((string, string) obj)
            => HashCode.Combine(obj.Item1, obj.Item2);
    }
}
