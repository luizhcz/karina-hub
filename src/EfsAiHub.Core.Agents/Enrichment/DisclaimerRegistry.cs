namespace EfsAiHub.Core.Agents.Enrichment;

/// <summary>
/// Registry estático de textos de disclaimer por chave.
/// Determinístico, auditável, não depende de LLM.
/// Usado pelo GenericEnricher para anexar disclaimers regulatórios às respostas.
/// </summary>
public static class DisclaimerRegistry
{
    private static readonly Dictionary<string, string> Disclaimers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["regulatorio_cvm"] = "\n\n⚠️ Valores estimados com base em conhecimento histórico. Consulte dados em tempo real antes de operar.",
    };

    /// <summary>Obtém o texto do disclaimer pela chave. Retorna null se não encontrado.</summary>
    public static string? Get(string key) =>
        Disclaimers.TryGetValue(key, out var text) ? text : null;

    /// <summary>Verifica se a chave existe no registry.</summary>
    public static bool Contains(string key) => Disclaimers.ContainsKey(key);
}
